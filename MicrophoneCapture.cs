using UnityEngine;
using System.Collections;

public class MicrophoneCapture : MonoBehaviour
{
    [Header("Configurações Hands-Free")]

    [Tooltip("Volume mínimo necessário para considerar que existe fala.")]
    public float threshold = 0.015f;

    [Tooltip("Tempo de silêncio necessário para finalizar a frase.")]
    public float silenceLimit = 1.5f;

    [Tooltip("Frequência de captura do dispositivo.")]
    public int sampleRate = 44100;

    [Tooltip("Tamanho do buffer circular do microfone em segundos.")]
    public int bufferLength = 10;


    // ============================================================
    // DETECÇÃO
    // ============================================================

    [Header("Detecção de Voz")]

    [Tooltip(
        "Quantidade de áudio anterior ao início detectado que será " +
        "incluída na gravação. Evita cortar o começo das palavras."
    )]
    public float preRollSeconds = 0.25f;

    [Tooltip(
        "Duração máxima de uma fala antes de ser processada automaticamente."
    )]
    public float maxRecordingSeconds = 8f;


    // ============================================================
    // GATE DE QUALIDADE
    // ============================================================

    [Header("Gate de Qualidade (Anti Falso-Positivo)")]

    public bool enableQualityGate = true;

    public float minDurationSeconds = 0.4f;

    public float minAvgEnergy = 0.0015f;


    // ============================================================
    // NORMALIZAÇÃO
    // ============================================================

    [Header("Normalização")]

    [Tooltip(
        "Normaliza o áudio somente quando o pico estiver abaixo deste valor."
    )]
    public bool enableNormalization = true;

    [Tooltip("Pico desejado após normalização.")]
    [Range(0.1f, 1f)]
    public float targetPeak = 0.85f;

    [Tooltip(
        "Não aplica normalização quando o pico estiver abaixo deste valor."
    )]
    public float minPeakToNormalize = 0.005f;


    // ============================================================
    // DEBUG
    // ============================================================

    [Header("Debug")]

    public bool saveDebugWav = false;

    public bool verboseLogging = true;


    // ============================================================
    // WO MIC
    // ============================================================

    [Header("WO Mic")]

    [SerializeField]
    private string preferredMicrophone = "WO Mic Device";

    [Tooltip(
        "Canal utilizado quando o WO Mic entregar áudio estéreo.\n" +
        "0 = esquerdo\n" +
        "1 = direito"
    )]
    [SerializeField]
    private int microphoneChannel = 0;


    // ============================================================
    // AZURE STT
    // ============================================================

    [Header("Azure STT")]

    [SerializeField]
    private AzureSTTUnity azureSTT;


    // ============================================================
    // CONTROLE INTERNO
    // ============================================================

    private AudioClip recordingClip;

    private string micDevice;

    private bool isRecording = false;

    private bool canListen = true;

    private bool processingAudio = false;

    private bool blockVoiceRecognition = false;

    private float silenceTimer = 0f;

    private float recordingTimer = 0f;

    // Posição em frames onde a fala começou.
    private int recordingStartPosition = -1;

    // Posição mais recente conhecida.
    private int lastMicrophonePosition = -1;

    // Tempo usado para evitar capturar imediatamente o final
    // da voz da Nyra quando o reconhecimento é liberado.
    private float recognitionCooldownUntil = 0f;


    // ============================================================
    // START
    // ============================================================

    private void Start()
    {
        Application.runInBackground = true;

        string[] devices = Microphone.devices;

        if (devices == null || devices.Length == 0)
        {
            Debug.LogError(
                "❌ [NYRA] Nenhum microfone detectado pelo Unity!"
            );

            return;
        }


        Debug.Log(
            "🎤 [NYRA] Microfones encontrados: " +
            devices.Length
        );


        for (int i = 0; i < devices.Length; i++)
        {
            Debug.Log(
                "🎤 [NYRA] Microfone [" +
                i +
                "]: " +
                devices[i]
            );
        }


        // ========================================================
        // SELECIONAR WO MIC
        // ========================================================

        micDevice = null;


        for (int i = 0; i < devices.Length; i++)
        {
            if (
                devices[i].IndexOf(
                    preferredMicrophone,
                    System.StringComparison.OrdinalIgnoreCase
                ) >= 0
            )
            {
                micDevice = devices[i];
                break;
            }
        }


        if (!string.IsNullOrEmpty(micDevice))
        {
            Debug.Log(
                "🎯 [NYRA] WO Mic encontrado e selecionado: " +
                micDevice
            );
        }
        else
        {
            micDevice = devices[0];

            Debug.LogWarning(
                "⚠️ [NYRA] WO Mic Device não encontrado.\n" +
                "Usando o primeiro microfone disponível: " +
                micDevice
            );
        }


        // ========================================================
        // AZURE
        // ========================================================

        if (azureSTT == null)
        {
            Debug.LogError(
                "❌ [NYRA] AzureSTTUnity não está conectado no Inspector!"
            );
        }
        else
        {
            Debug.Log(
                "✅ [NYRA] AzureSTTUnity conectado."
            );
        }


        StartCoroutine(StartMicWithDelay());
    }


    // ============================================================
    // INICIAR MICROFONE
    // ============================================================

    private IEnumerator StartMicWithDelay()
    {
        yield return null;

        StartMicrophoneInternal();
    }


    private bool StartMicrophoneInternal()
    {
        if (string.IsNullOrEmpty(micDevice))
        {
            Debug.LogError(
                "❌ [NYRA] Nenhum dispositivo de microfone selecionado."
            );

            return false;
        }


        if (Microphone.IsRecording(micDevice))
        {
            return true;
        }


        recordingClip =
            Microphone.Start(
                micDevice,
                true,
                bufferLength,
                sampleRate
            );


        if (recordingClip == null)
        {
            Debug.LogError(
                "❌ [NYRA] Não foi possível iniciar o microfone."
            );

            return false;
        }


        isRecording = false;
        processingAudio = false;

        silenceTimer = 0f;
        recordingTimer = 0f;

        recordingStartPosition = -1;


        lastMicrophonePosition =
            Microphone.GetPosition(micDevice);


        Debug.Log(
            "🎤 [NYRA] Microfone iniciado: " +
            micDevice
        );


        Debug.Log(
            "🎧 [NYRA] Formato REAL recebido pelo Unity: " +
            recordingClip.frequency +
            " Hz / " +
            recordingClip.channels +
            " canal(is)"
        );


        // ========================================================
        // CANAL
        // ========================================================

        if (recordingClip.channels > 1)
        {
            microphoneChannel =
                Mathf.Clamp(
                    microphoneChannel,
                    0,
                    recordingClip.channels - 1
                );


            Debug.Log(
                "🎚️ [NYRA] WO Mic possui " +
                recordingClip.channels +
                " canais.\n" +
                "Usando canal: " +
                microphoneChannel
            );
        }
        else
        {
            microphoneChannel = 0;

            Debug.Log(
                "🎚️ [NYRA] WO Mic está entregando MONO."
            );
        }


        // ========================================================
        // FREQUÊNCIA
        // ========================================================

        if (recordingClip.frequency != sampleRate)
        {
            Debug.LogWarning(
                "⚠️ [NYRA] Frequência real diferente da configurada.\n" +
                "Real: " +
                recordingClip.frequency +
                " Hz | " +
                "Configurada: " +
                sampleRate +
                " Hz."
            );
        }
        else
        {
            Debug.Log(
                "✅ [NYRA] Frequência do microfone: " +
                sampleRate +
                " Hz."
            );
        }


        return true;
    }


    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        if (!canListen)
            return;


        if (recordingClip == null)
            return;


        if (string.IsNullOrEmpty(micDevice))
            return;


        if (!Microphone.IsRecording(micDevice))
            return;


        if (processingAudio)
            return;


        // ========================================================
        // NYRA ESTÁ FALANDO
        // ========================================================

        if (blockVoiceRecognition)
        {
            ResetDetectionState();

            lastMicrophonePosition =
                Microphone.GetPosition(micDevice);

            return;
        }


        // ========================================================
        // PEQUENO COOLDOWN APÓS LIBERAR
        // ========================================================

        if (Time.time < recognitionCooldownUntil)
        {
            ResetDetectionState();

            lastMicrophonePosition =
                Microphone.GetPosition(micDevice);

            return;
        }


        // ========================================================
        // POSIÇÃO ATUAL
        // ========================================================

        int currentPosition =
            Microphone.GetPosition(micDevice);


        if (currentPosition < 0)
            return;


        lastMicrophonePosition =
            currentPosition;


        // ========================================================
        // VOLUME
        // ========================================================

        float currentVolume =
            GetMaxVolume();


        // ========================================================
        // DETECTOU VOZ
        // ========================================================

        if (currentVolume > threshold)
        {
            if (!isRecording)
            {
                BeginVoiceRecording(
                    currentPosition
                );
            }


            silenceTimer = 0f;

            recordingTimer +=
                Time.deltaTime;


            // ====================================================
            // LIMITE MÁXIMO
            // ====================================================

            if (recordingTimer >= maxRecordingSeconds)
            {
                Debug.Log(
                    "⏱️ [NYRA] Duração máxima atingida. " +
                    "Processando áudio..."
                );

                ProcessAndSend();
            }


            return;
        }


        // ========================================================
        // SILÊNCIO DURANTE UMA FALA
        // ========================================================

        if (isRecording)
        {
            silenceTimer +=
                Time.deltaTime;

            recordingTimer +=
                Time.deltaTime;


            if (recordingTimer >= maxRecordingSeconds)
            {
                Debug.Log(
                    "⏱️ [NYRA] Duração máxima atingida. " +
                    "Processando áudio..."
                );

                ProcessAndSend();

                return;
            }


            if (silenceTimer >= silenceLimit)
            {
                Debug.Log(
                    "✅ [NYRA] Silêncio detectado. " +
                    "Finalizando fala..."
                );

                ProcessAndSend();
            }
        }
    }


    // ============================================================
    // INÍCIO DA FALA
    // ============================================================

    private void BeginVoiceRecording(
        int currentPosition
    )
    {
        isRecording = true;

        silenceTimer = 0f;

        recordingTimer = 0f;


        int totalSamples =
            recordingClip.samples;


        int preRollSamples =
            Mathf.RoundToInt(
                preRollSeconds *
                recordingClip.frequency
            );


        preRollSamples =
            Mathf.Clamp(
                preRollSamples,
                0,
                totalSamples - 1
            );


        recordingStartPosition =
            currentPosition -
            preRollSamples;


        if (recordingStartPosition < 0)
        {
            recordingStartPosition +=
                totalSamples;
        }


        if (verboseLogging)
        {
            Debug.Log(
                "🎙️ [NYRA] Ouvi você! Capturando...\n" +
                "Posição atual: " +
                currentPosition +
                "\nInício com pré-roll: " +
                recordingStartPosition
            );
        }
    }


    // ============================================================
    // RESET
    // ============================================================

    private void ResetDetectionState()
    {
        isRecording = false;

        silenceTimer = 0f;

        recordingTimer = 0f;

        recordingStartPosition = -1;
    }


    // ============================================================
    // BLOQUEAR / LIBERAR RECONHECIMENTO
    // ============================================================

    public void SetVoiceRecognitionBlocked(
        bool blocked
    )
    {
        blockVoiceRecognition =
            blocked;


        ResetDetectionState();


        if (blocked)
        {
            recognitionCooldownUntil =
                0f;


            Debug.Log(
                "🔇 [NYRA] Reconhecimento de voz BLOQUEADO.\n" +
                "🎤 Microfone continua fisicamente ligado."
            );
        }
        else
        {
            // Pequeno intervalo para não capturar
            // imediatamente o final da voz da Nyra.
            recognitionCooldownUntil =
                Time.time + 0.35f;


            // Atualiza a posição atual.
            if (
                !string.IsNullOrEmpty(micDevice) &&
                Microphone.IsRecording(micDevice)
            )
            {
                lastMicrophonePosition =
                    Microphone.GetPosition(micDevice);
            }


            Debug.Log(
                "🎤 [NYRA] Reconhecimento de voz LIBERADO."
            );
        }
    }


    // ============================================================
    // VOLUME
    // ============================================================

    private float GetMaxVolume()
    {
        if (recordingClip == null)
            return 0f;


        if (
            string.IsNullOrEmpty(micDevice) ||
            !Microphone.IsRecording(micDevice)
        )
        {
            return 0f;
        }


        int sampleWindow = 128;

        int channels =
            recordingClip.channels;


        float[] samples =
            new float[
                sampleWindow * channels
            ];


        int position =
            Microphone.GetPosition(
                micDevice
            );


        if (position <= 0)
            return 0f;


        int totalSamples =
            recordingClip.samples;


        int startRead =
            (
                position -
                sampleWindow +
                totalSamples
            ) %
            totalSamples;


        try
        {
            bool success =
                recordingClip.GetData(
                    samples,
                    startRead
                );


            if (!success)
                return 0f;


            int selectedChannel =
                Mathf.Clamp(
                    microphoneChannel,
                    0,
                    channels - 1
                );


            float maxVolume = 0f;


            for (
                int i = 0;
                i < sampleWindow;
                i++
            )
            {
                float value =
                    Mathf.Abs(
                        samples[
                            i * channels +
                            selectedChannel
                        ]
                    );


                if (value > maxVolume)
                {
                    maxVolume = value;
                }
            }


            return maxVolume;
        }
        catch
        {
            return 0f;
        }
    }


    // ============================================================
    // CAPTURAR E ENVIAR
    // ============================================================

    private void ProcessAndSend()
    {
        if (processingAudio)
            return;


        processingAudio = true;


        isRecording = false;

        silenceTimer = 0f;

        recordingTimer = 0f;


        // ========================================================
        // BLOQUEIO
        // ========================================================

        if (blockVoiceRecognition)
        {
            Debug.Log(
                "🔇 [NYRA] Processamento cancelado: " +
                "reconhecimento bloqueado."
            );


            recordingStartPosition = -1;

            processingAudio = false;

            return;
        }


        if (recordingClip == null)
        {
            processingAudio = false;

            return;
        }


        if (
            string.IsNullOrEmpty(micDevice) ||
            !Microphone.IsRecording(micDevice)
        )
        {
            Debug.LogWarning(
                "⚠️ [NYRA] Microfone não está gravando."
            );


            processingAudio = false;

            return;
        }


        if (recordingStartPosition < 0)
        {
            Debug.LogWarning(
                "⚠️ [NYRA] Posição inicial da gravação inválida."
            );


            processingAudio = false;

            return;
        }


        int endPosition =
            Microphone.GetPosition(
                micDevice
            );


        if (endPosition < 0)
        {
            Debug.LogWarning(
                "⚠️ [NYRA] Posição final do microfone inválida."
            );


            processingAudio = false;

            return;
        }


        try
        {
            // ====================================================
            // CONFIGURAÇÃO
            // ====================================================

            int channels =
                recordingClip.channels;


            int realSampleRate =
                recordingClip.frequency;


            int totalFrames =
                recordingClip.samples;


            // ====================================================
            // QUANTIDADE DE FRAMES
            //
            // Trata corretamente a volta do buffer circular.
            // ====================================================

            int frameCount;


            if (endPosition >= recordingStartPosition)
            {
                frameCount =
                    endPosition -
                    recordingStartPosition;
            }
            else
            {
                frameCount =
                    (
                        totalFrames -
                        recordingStartPosition
                    ) +
                    endPosition;
            }


            // ====================================================
            // SEGURANÇA
            // ====================================================

            if (frameCount <= 0)
            {
                Debug.LogWarning(
                    "⚠️ [NYRA] Nenhum áudio válido encontrado."
                );


                processingAudio = false;

                recordingStartPosition = -1;

                return;
            }


            // Nunca permitir ler mais que o buffer inteiro.
            frameCount =
                Mathf.Clamp(
                    frameCount,
                    1,
                    totalFrames
                );


            // ====================================================
            // CAPTURA
            // ====================================================

            int interleavedSamples =
                frameCount *
                channels;


            float[] samples =
                new float[
                    interleavedSamples
                ];


            bool success =
                recordingClip.GetData(
                    samples,
                    recordingStartPosition
                );


            if (!success)
            {
                Debug.LogError(
                    "❌ [NYRA] AudioClip.GetData falhou."
                );


                processingAudio = false;

                recordingStartPosition = -1;

                return;
            }


            Debug.Log(
                "🚀 [NYRA] Trecho de fala capturado.\n" +
                "Frames: " +
                frameCount +
                "\n" +
                "Samples intercalados: " +
                samples.Length +
                "\n" +
                "Início: " +
                recordingStartPosition +
                "\n" +
                "Fim: " +
                endPosition
            );


            Debug.Log(
                "🎧 [NYRA] Frequência REAL: " +
                realSampleRate +
                " Hz"
            );


            Debug.Log(
                "🎚️ [NYRA] Canais: " +
                channels
            );


            // ====================================================
            // DIAGNÓSTICO DOS CANAIS
            // ====================================================

            if (channels > 1)
            {
                float[] channelPeaks =
                    new float[channels];


                int frames =
                    samples.Length /
                    channels;


                for (
                    int i = 0;
                    i < frames;
                    i++
                )
                {
                    for (
                        int c = 0;
                        c < channels;
                        c++
                    )
                    {
                        float value =
                            Mathf.Abs(
                                samples[
                                    i * channels +
                                    c
                                ]
                            );


                        if (
                            value >
                            channelPeaks[c]
                        )
                        {
                            channelPeaks[c] =
                                value;
                        }
                    }
                }


                for (
                    int c = 0;
                    c < channels;
                    c++
                )
                {
                    Debug.Log(
                        "🎚️ [NYRA] Canal " +
                        c +
                        " | Pico: " +
                        channelPeaks[c]
                            .ToString("0.000000")
                    );
                }
            }


            // ====================================================
            // MONO
            // ====================================================

            float[] monoSamples;


            if (channels == 1)
            {
                monoSamples =
                    samples;
            }
            else
            {
                monoSamples =
                    ConvertToMono(
                        samples,
                        channels
                    );
            }


            Debug.Log(
                "🔉 [NYRA] Áudio mono: " +
                monoSamples.Length +
                " samples."
            );


            // ====================================================
            // RESAMPLE
            // ====================================================

            const int azureSampleRate =
                16000;


            float[] finalSamples;


            if (
                realSampleRate !=
                azureSampleRate
            )
            {
                Debug.Log(
                    "🔄 [NYRA] Convertendo " +
                    realSampleRate +
                    " Hz → " +
                    azureSampleRate +
                    " Hz..."
                );


                finalSamples =
                    ResampleAudio(
                        monoSamples,
                        realSampleRate,
                        azureSampleRate
                    );


                Debug.Log(
                    "✅ [NYRA] Resample concluído: " +
                    finalSamples.Length +
                    " samples."
                );
            }
            else
            {
                finalSamples =
                    monoSamples;
            }


            if (
                finalSamples == null ||
                finalSamples.Length == 0
            )
            {
                Debug.LogWarning(
                    "⚠️ [NYRA] Áudio final vazio."
                );


                processingAudio = false;

                recordingStartPosition = -1;

                return;
            }


            // ====================================================
            // DURAÇÃO
            // ====================================================

            float durationSeconds =
                (float)finalSamples.Length /
                azureSampleRate;


            // ====================================================
            // MÉTRICAS ANTES DA NORMALIZAÇÃO
            // ====================================================

            float currentPeak =
                0f;


            float sumAbs =
                0f;


            for (
                int i = 0;
                i < finalSamples.Length;
                i++
            )
            {
                float abs =
                    Mathf.Abs(
                        finalSamples[i]
                    );


                if (abs > currentPeak)
                {
                    currentPeak =
                        abs;
                }


                sumAbs +=
                    abs;
            }


            float avgEnergy =
                finalSamples.Length > 0
                    ? sumAbs /
                      finalSamples.Length
                    : 0f;


            Debug.Log(
                "📏 [NYRA] Duração: " +
                durationSeconds.ToString("0.00") +
                "s"
            );


            Debug.Log(
                "🔎 [NYRA] Pico bruto: " +
                currentPeak.ToString("0.0000")
            );


            Debug.Log(
                "🔎 [NYRA] Energia média bruta: " +
                avgEnergy.ToString("0.000000")
            );


            // ====================================================
            // QUALITY GATE
            //
            // IMPORTANTE:
            // Agora acontece ANTES da normalização.
            // ====================================================

            if (
                enableQualityGate &&
                (
                    durationSeconds <
                    minDurationSeconds ||
                    avgEnergy <
                    minAvgEnergy
                )
            )
            {
                Debug.LogWarning(
                    "🚫 [NYRA] Áudio descartado antes do Azure.\n" +
                    "Duração = " +
                    durationSeconds.ToString("0.00") +
                    "s\n" +
                    "Energia = " +
                    avgEnergy.ToString("0.000000")
                );


                recordingStartPosition = -1;

                processingAudio = false;

                return;
            }


            // ====================================================
            // NORMALIZAÇÃO
            // ====================================================

            if (
                enableNormalization &&
                currentPeak >
                minPeakToNormalize &&
                currentPeak <
                targetPeak
            )
            {
                float gain =
                    targetPeak /
                    currentPeak;


                // Limitar ganho para evitar
                // transformar ruído em áudio enorme.
                gain =
                    Mathf.Min(
                        gain,
                        4f
                    );


                for (
                    int i = 0;
                    i < finalSamples.Length;
                    i++
                )
                {
                    finalSamples[i] =
                        Mathf.Clamp(
                            finalSamples[i] *
                            gain,
                            -1f,
                            1f
                        );
                }


                Debug.Log(
                    "🔊 [NYRA] Normalização aplicada.\n" +
                    "Ganho: x" +
                    gain.ToString("0.00")
                );
            }


            // ====================================================
            // PCM16
            // ====================================================

            byte[] pcmData =
                GetPCM16(
                    finalSamples
                );


            if (
                pcmData == null ||
                pcmData.Length == 0
            )
            {
                Debug.LogError(
                    "❌ [NYRA] PCM vazio."
                );


                recordingStartPosition = -1;

                processingAudio = false;

                return;
            }


            Debug.Log(
                "🔊 [NYRA] PCM16 gerado: " +
                pcmData.Length +
                " bytes."
            );


            // ====================================================
            // WAV DEBUG
            // ====================================================

            if (saveDebugWav)
            {
                string debugPath =
                    System.IO.Path.Combine(
                        Application.persistentDataPath,
                        "nyra_debug_" +
                        System.DateTime.Now.ToString(
                            "yyyyMMdd_HHmmss"
                        ) +
                        ".wav"
                    );


                SaveDebugWav(
                    debugPath,
                    pcmData,
                    azureSampleRate
                );


                Debug.Log(
                    "💾 [NYRA] WAV salvo:\n" +
                    debugPath
                );
            }


            // ====================================================
            // VERIFICAÇÃO FINAL
            // ====================================================

            if (blockVoiceRecognition)
            {
                Debug.Log(
                    "🔇 [NYRA] Áudio NÃO enviado.\n" +
                    "Nyra começou a falar durante o processamento."
                );


                recordingStartPosition = -1;

                processingAudio = false;

                return;
            }


            // ====================================================
            // AZURE
            // ====================================================

            Debug.Log(
                "📐 [NYRA] PCM enviado como:\n" +
                azureSampleRate +
                " Hz / 16 bit / mono"
            );


            if (azureSTT == null)
            {
                Debug.LogError(
                    "❌ [NYRA] AzureSTTUnity NÃO está conectado!"
                );
            }
            else
            {
                azureSTT.SendAudio(
                    pcmData
                );


                Debug.Log(
                    "📤 [NYRA] Áudio enviado para Azure STT."
                );
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                "❌ [NYRA] Erro processando áudio:"
            );


            Debug.LogError(
                e.ToString()
            );
        }


        // ========================================================
        // FINALIZAÇÃO
        //
        // NÃO reinicia o microfone.
        // Ele continua rodando.
        // ========================================================

        recordingStartPosition = -1;

        processingAudio = false;
    }


    // ============================================================
    // RESAMPLE
    // ============================================================

    private float[] ResampleAudio(
        float[] original,
        int originalRate,
        int targetRate
    )
    {
        if (
            original == null ||
            original.Length == 0
        )
        {
            return new float[0];
        }


        if (
            originalRate <= 0 ||
            targetRate <= 0
        )
        {
            return original;
        }


        if (
            originalRate ==
            targetRate
        )
        {
            return original;
        }


        int newLength =
            Mathf.CeilToInt(
                (float)original.Length *
                targetRate /
                originalRate
            );


        if (newLength <= 0)
        {
            return new float[0];
        }


        float[] resampled =
            new float[newLength];


        float ratio =
            (float)(
                original.Length - 1
            ) /
            Mathf.Max(
                1,
                newLength - 1
            );


        for (
            int i = 0;
            i < newLength;
            i++
        )
        {
            float position =
                i * ratio;


            int indexFloor =
                Mathf.FloorToInt(
                    position
                );


            int indexCeil =
                Mathf.Min(
                    indexFloor + 1,
                    original.Length - 1
                );


            float t =
                position -
                indexFloor;


            resampled[i] =
                Mathf.Lerp(
                    original[indexFloor],
                    original[indexCeil],
                    t
                );
        }


        return resampled;
    }


    // ============================================================
    // CONVERTER PARA MONO
    // ============================================================

    private float[] ConvertToMono(
        float[] samples,
        int channels
    )
    {
        if (
            samples == null ||
            samples.Length == 0 ||
            channels <= 0
        )
        {
            return new float[0];
        }


        if (channels == 1)
        {
            return samples;
        }


        int selectedChannel =
            Mathf.Clamp(
                microphoneChannel,
                0,
                channels - 1
            );


        int monoLength =
            samples.Length /
            channels;


        float[] mono =
            new float[monoLength];


        for (
            int i = 0;
            i < monoLength;
            i++
        )
        {
            mono[i] =
                samples[
                    i * channels +
                    selectedChannel
                ];
        }


        Debug.Log(
            "🎯 [NYRA] Extraindo SOMENTE o canal " +
            selectedChannel +
            " do WO Mic."
        );


        return mono;
    }


    // ============================================================
    // PCM16
    // ============================================================

    public byte[] GetPCM16(
        float[] samples
    )
    {
        if (
            samples == null ||
            samples.Length == 0
        )
        {
            return null;
        }


        byte[] pcm16Data =
            new byte[
                samples.Length * 2
            ];


        for (
            int i = 0;
            i < samples.Length;
            i++
        )
        {
            float sample =
                Mathf.Clamp(
                    samples[i],
                    -1f,
                    1f
                );


            short value =
                (short)(
                    sample *
                    32767f
                );


            pcm16Data[
                i * 2
            ] =
                (byte)(
                    value &
                    0xFF
                );


            pcm16Data[
                i * 2 + 1
            ] =
                (byte)(
                    (value >> 8) &
                    0xFF
                );
        }


        return pcm16Data;
    }


    // ============================================================
    // WAV DEBUG
    // ============================================================

    private void SaveDebugWav(
        string path,
        byte[] pcm16Data,
        int wavSampleRate
    )
    {
        try
        {
            using (
                var fs =
                    new System.IO.FileStream(
                        path,
                        System.IO.FileMode.Create
                    )
            )
            using (
                var bw =
                    new System.IO.BinaryWriter(
                        fs
                    )
            )
            {
                int byteRate =
                    wavSampleRate * 2;


                bw.Write(
                    new char[4]
                    {
                        'R',
                        'I',
                        'F',
                        'F'
                    }
                );


                bw.Write(
                    36 +
                    pcm16Data.Length
                );


                bw.Write(
                    new char[4]
                    {
                        'W',
                        'A',
                        'V',
                        'E'
                    }
                );


                bw.Write(
                    new char[4]
                    {
                        'f',
                        'm',
                        't',
                        ' '
                    }
                );


                bw.Write(16);

                bw.Write((short)1);

                bw.Write((short)1);

                bw.Write(
                    wavSampleRate
                );

                bw.Write(
                    byteRate
                );

                bw.Write((short)2);

                bw.Write((short)16);


                bw.Write(
                    new char[4]
                    {
                        'd',
                        'a',
                        't',
                        'a'
                    }
                );


                bw.Write(
                    pcm16Data.Length
                );


                bw.Write(
                    pcm16Data
                );
            }
        }
        catch (
            System.Exception e
        )
        {
            Debug.LogError(
                "❌ [NYRA] Erro salvando WAV: " +
                e
            );
        }
    }


    // ============================================================
    // REINICIAR MICROFONE
    //
    // Mantido para compatibilidade caso outro script chame
    // esse método futuramente.
    // ============================================================

    private void RestartMicrophone()
    {
        if (string.IsNullOrEmpty(micDevice))
            return;


        try
        {
            if (
                Microphone.IsRecording(
                    micDevice
                )
            )
            {
                Microphone.End(
                    micDevice
                );
            }
        }
        catch
        {
        }


        recordingClip = null;


        StartMicrophoneInternal();
    }


    // ============================================================
    // MUTE
    // ============================================================

    public void SetMute(
        bool state
    )
    {
        canListen =
            !state;


        if (state)
        {
            ResetDetectionState();


            if (
                !string.IsNullOrEmpty(
                    micDevice
                )
            )
            {
                try
                {
                    if (
                        Microphone.IsRecording(
                            micDevice
                        )
                    )
                    {
                        Microphone.End(
                            micDevice
                        );
                    }
                }
                catch
                {
                }
            }


            recordingClip = null;


            Debug.Log(
                "🔇 [NYRA] Microfone desligado."
            );
        }
        else
        {
            if (
                !string.IsNullOrEmpty(
                    micDevice
                ) &&
                !Microphone.IsRecording(
                    micDevice
                )
            )
            {
                StartMicrophoneInternal();
            }


            Debug.Log(
                "🔊 [NYRA] Microfone ligado."
            );
        }
    }


    // ============================================================
    // DESTROY
    // ============================================================

    private void OnDestroy()
    {
        if (
            !string.IsNullOrEmpty(
                micDevice
            )
        )
        {
            try
            {
                if (
                    Microphone.IsRecording(
                        micDevice
                    )
                )
                {
                    Microphone.End(
                        micDevice
                    );
                }
            }
            catch
            {
            }
        }
    }
}