package pl.sentinelx.companion

import android.annotation.SuppressLint
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioRecord
import android.media.AudioTrack
import android.media.MediaRecorder

/**
 * Bramka audio relacji głośnika (UCZCIWA charakterystyka: Android nie daje zwykłym aplikacjom
 * audio rozmowy SIM, więc rozmówca jest słyszany przez mikrofon z głośnika telefonu).
 * Mikrofon: 16 kHz mono → most (audio up). Głośnik: mowa TTS z PC → AudioTrack (audio down).
 */
class AudioGateway(
    private val audioManager: AudioManager,
    private val onUpChunk: (ShortArray) -> Unit,
) {
    companion object {
        const val SAMPLE_RATE = 16000
        private const val CHUNK_FRAMES = 1600 // 100 ms
    }

    @Volatile private var running = false
    private var record: AudioRecord? = null
    private var track: AudioTrack? = null
    private var upThread: Thread? = null

    /** Włącza głośnik (relacja rozmowy) i startuje odbiór mikrofonu. */
    @SuppressLint("MissingPermission")
    fun start() {
        if (running) return
        running = true
        try { audioManager.mode = AudioManager.MODE_IN_CALL } catch (_: Exception) {}
        try { audioManager.isSpeakerphoneOn = true } catch (_: Exception) {}
        val minBuf = AudioRecord.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        record = AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION, SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(minBuf, CHUNK_FRAMES * 4))
        record?.startRecording()
        upThread = Thread {
            val buffer = ShortArray(CHUNK_FRAMES)
            while (running) {
                val read = record?.read(buffer, 0, buffer.size) ?: -1
                if (read > 0) onUpChunk(buffer.copyOf(read))
            }
        }.also { it.priority = Thread.MAX_PRIORITY; it.start() }

        val minOut = AudioTrack.getMinBufferSize(SAMPLE_RATE, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT)
        track = AudioTrack(AudioManager.STREAM_VOICE_CALL, SAMPLE_RATE,
            AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT, maxOf(minOut, CHUNK_FRAMES * 4),
            AudioTrack.MODE_STREAM)
        track?.play()
    }

    /** Odtwarza mowę Sentinela przez głośnik (audio down z mostu). */
    fun playDown(pcm: ShortArray) {
        track?.write(pcm, 0, pcm.size)
    }

    fun stop() {
        running = false
        try { record?.stop() } catch (_: Exception) {}
        try { record?.release() } catch (_: Exception) {}
        try { track?.stop() } catch (_: Exception) {}
        try { track?.release() } catch (_: Exception) {}
        record = null; track = null; upThread = null
        try { audioManager.isSpeakerphoneOn = false } catch (_: Exception) {}
        try { audioManager.mode = AudioManager.MODE_NORMAL } catch (_: Exception) {}
    }
}
