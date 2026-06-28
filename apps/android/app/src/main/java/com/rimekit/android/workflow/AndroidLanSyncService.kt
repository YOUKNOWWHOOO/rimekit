package com.rimekit.android.workflow

import com.rimekit.android.artifacts.AndroidArtifactService
import com.rimekit.android.artifacts.AndroidConfigModel
import com.rimekit.android.artifacts.AndroidConfigRepository
import org.json.JSONObject
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.util.Collections
import kotlin.concurrent.thread

internal class AndroidLanSyncService(
    private val artifactService: AndroidArtifactService,
    private val configRepository: AndroidConfigRepository,
    private val currentImportRootProvider: () -> String?,
    private val onImportedSnapshot: (String) -> Unit = {},
) {
    @Volatile
    private var running = false

    @Volatile
    private var httpPort = 0

    private var httpServerSocket: ServerSocket? = null
    private var discoverySocket: DatagramSocket? = null

    fun start() {
        if (running) {
            return
        }

        httpServerSocket = ServerSocket().apply {
            reuseAddress = true
            bind(InetSocketAddress(0))
            httpPort = localPort
        }
        running = true

        thread(name = "rimekit-android-lan-sync-http", isDaemon = true) {
            runHttpServer()
        }
        thread(name = "rimekit-android-lan-sync-discovery", isDaemon = true) {
            runDiscoveryServer()
        }
    }

    fun stop() {
        running = false
        try {
            discoverySocket?.close()
        } catch (_: Exception) {
        }
        try {
            httpServerSocket?.close()
        } catch (_: Exception) {
        }
    }

    private fun runHttpServer() {
        val serverSocket = httpServerSocket ?: return
        while (running) {
            val client = try {
                serverSocket.accept()
            } catch (_: Exception) {
                break
            }

            thread(name = "rimekit-android-lan-sync-client", isDaemon = true) {
                client.use { socket ->
                    handleClient(socket)
                }
            }
        }
    }

    private fun runDiscoveryServer() {
        val socket = DatagramSocket(DISCOVERY_PORT).apply {
            broadcast = true
            soTimeout = 0
        }
        discoverySocket = socket
        val buffer = ByteArray(512)

        while (running) {
            val packet = try {
                DatagramPacket(buffer, buffer.size).also(socket::receive)
            } catch (_: Exception) {
                break
            }

            val message = String(packet.data, 0, packet.length, Charsets.UTF_8).trim()
            if (message != DISCOVERY_REQUEST) {
                continue
            }

            val endpoint = getAdvertisedEndpoint() ?: continue
            val payload = "$DISCOVERY_REPLY_PREFIX$endpoint".toByteArray(Charsets.UTF_8)
            val reply = DatagramPacket(payload, payload.size, packet.address, packet.port)
            try {
                socket.send(reply)
            } catch (_: Exception) {
                // Best-effort only.
            }
        }
    }

    private fun handleClient(socket: Socket) {
        val input = BufferedInputStream(socket.getInputStream())
        val output = BufferedOutputStream(socket.getOutputStream())

        val requestLine = readHttpLine(input) ?: return
        if (requestLine.isBlank()) {
            return
        }

        val parts = requestLine.split(' ')
        if (parts.size < 2) {
            writeJsonResponse(output, 400, JSONObject().put("error", "invalid_request_line"))
            return
        }

        val method = parts[0]
        val path = parts[1]
        val headers = mutableMapOf<String, String>()
        while (true) {
            val line = readHttpLine(input) ?: break
            if (line.isBlank()) {
                break
            }
            val separatorIndex = line.indexOf(':')
            if (separatorIndex > 0) {
                headers[line.substring(0, separatorIndex).trim().lowercase()] = line.substring(separatorIndex + 1).trim()
            }
        }

        try {
            when {
                method == "GET" && path == "/api/lan-sync/status" -> {
                    val response = buildStatusPayload()
                    writeJsonResponse(output, 200, response)
                }

                method == "GET" && path == "/api/lan-sync/snapshot/latest" -> {
                    val configModel = configRepository.load()
                    val tempFile = File.createTempFile("rimekit-android-sync-", ".zip")
                    val snapshotId = artifactService.exportLatestSnapshotToFile(
                        targetFile = tempFile,
                        configModel = configModel,
                        importRootUri = currentImportRootProvider(),
                    )
                    writeBinaryResponse(
                        output = output,
                        statusCode = 200,
                        contentType = "application/zip",
                        payload = tempFile.readBytes(),
                        extraHeaders = mapOf("X-RimeKit-Snapshot-Id" to snapshotId),
                    )
                    tempFile.delete()
                }

                method == "POST" && path == "/api/lan-sync/snapshot/latest" -> {
                    val importRootUri = currentImportRootProvider()
                    require(!importRootUri.isNullOrBlank()) { "当前还没有 Android 导入源目录授权，不能接收局域网同步快照。" }

                    val contentLength = headers["content-length"]?.toIntOrNull()
                        ?: error("当前同步请求缺少有效的 Content-Length。")
                    val body = readFixedBytes(input, contentLength)
                    val tempFile = File.createTempFile("rimekit-android-import-", ".zip")
                    tempFile.writeBytes(body)

                    val stagedSnapshot = artifactService.stageSyncSnapshotImport(tempFile)
                    artifactService.backupImportRoot(importRootUri)
                    artifactService.applyStagedSyncSnapshot(importRootUri, stagedSnapshot)
                    configRepository.save(stagedSnapshot.configModel)
                    onImportedSnapshot("已通过第一方局域网同步接收并写入最新同步快照。")

                    writeJsonResponse(
                        output,
                        200,
                        JSONObject()
                            .put("status", "completed")
                            .put("snapshot_id", stagedSnapshot.snapshotId),
                    )
                    tempFile.delete()
                }

                else -> {
                    writeJsonResponse(output, 404, JSONObject().put("error", "not_found"))
                }
            }
        } catch (exception: Exception) {
            writeJsonResponse(
                output,
                500,
                JSONObject()
                    .put("error", "lan_sync_failed")
                    .put("detail", exception.message ?: "unknown_error"),
            )
        }
    }

    private fun buildStatusPayload(): JSONObject {
        val configModel = configRepository.load()
        val latestSnapshotId = artifactService.ensureLatestSnapshot(
            configModel = configModel,
            importRootUri = currentImportRootProvider(),
        )
        return JSONObject()
            .put("protocol_version", 1)
            .put("platform", "android")
            .put("latest_snapshot_id", latestSnapshotId)
            .put("can_export_snapshot", latestSnapshotId.isNotBlank())
            .put("can_import_snapshot", !currentImportRootProvider().isNullOrBlank())
    }

    private fun getAdvertisedEndpoint(): String? {
        val interfaces = try {
            Collections.list(NetworkInterface.getNetworkInterfaces())
        } catch (_: Exception) {
            emptyList()
        }

        val address = interfaces
            .asSequence()
            .filter { network -> network.isUp && !network.isLoopback && !network.isVirtual }
            .flatMap { network -> Collections.list(network.inetAddresses).asSequence() }
            .firstOrNull { address -> address is Inet4Address && !address.isLoopbackAddress }
            ?: return null
        return "http://${address.hostAddress}:$httpPort/"
    }

    private fun writeJsonResponse(
        output: BufferedOutputStream,
        statusCode: Int,
        payload: JSONObject,
    ) {
        writeBinaryResponse(
            output = output,
            statusCode = statusCode,
            contentType = "application/json; charset=utf-8",
            payload = payload.toString().toByteArray(Charsets.UTF_8),
        )
    }

    private fun writeBinaryResponse(
        output: BufferedOutputStream,
        statusCode: Int,
        contentType: String,
        payload: ByteArray,
        extraHeaders: Map<String, String> = emptyMap(),
    ) {
        val headerBuilder = StringBuilder()
        headerBuilder.append("HTTP/1.1 ").append(statusCode).append(' ').append(httpStatusLabel(statusCode)).append("\r\n")
        headerBuilder.append("Content-Type: ").append(contentType).append("\r\n")
        headerBuilder.append("Content-Length: ").append(payload.size).append("\r\n")
        headerBuilder.append("Connection: close\r\n")
        extraHeaders.forEach { (key, value) ->
            headerBuilder.append(key).append(": ").append(value).append("\r\n")
        }
        headerBuilder.append("\r\n")

        output.write(headerBuilder.toString().toByteArray(Charsets.UTF_8))
        output.write(payload)
        output.flush()
    }

    private fun readFixedBytes(input: BufferedInputStream, length: Int): ByteArray {
        val payload = ByteArray(length)
        var offset = 0
        while (offset < length) {
            val read = input.read(payload, offset, length - offset)
            if (read < 0) {
                error("同步请求体尚未完整读取。")
            }
            offset += read
        }
        return payload
    }

    private fun readHttpLine(input: BufferedInputStream): String? {
        val buffer = ByteArrayOutputStream()
        while (true) {
            val value = input.read()
            if (value < 0) {
                return if (buffer.size() == 0) null else buffer.toString(Charsets.UTF_8.name())
            }
            if (value == '\n'.code) {
                break
            }
            if (value != '\r'.code) {
                buffer.write(value)
            }
        }
        return buffer.toString(Charsets.UTF_8.name())
    }

    private fun httpStatusLabel(statusCode: Int): String {
        return when (statusCode) {
            200 -> "OK"
            400 -> "Bad Request"
            404 -> "Not Found"
            else -> "Internal Server Error"
        }
    }

    private companion object {
        const val DISCOVERY_PORT = 39292
        const val DISCOVERY_REQUEST = "RimeKitLanSync/1 DISCOVER"
        const val DISCOVERY_REPLY_PREFIX = "RimeKitLanSync/1 ENDPOINT "
    }
}
