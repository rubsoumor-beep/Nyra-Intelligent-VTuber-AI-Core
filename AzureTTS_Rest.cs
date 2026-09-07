using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class AzureTTS_Rest : MonoBehaviour
{
    [Header("Azure Speech")]
    public string speechRegion = "eastus";

    // A chave não é serializada pelo Unity.
    // Configure AZURE_SPEECH_KEY no ambiente do sistema.
    [NonSerialized]
    private string speechKey;

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Nyra Voice")]
    public string voiceName = "pt-BR-YaraNeural";

    [Header("Nyra - VTuber")]
    [Range(0.5f, 2.0f)]
    public float rate = 1.00f;

    [Range(-50f, 100f)]
    public float pitchPercent = 0f;

    public bool useGirlRole = false;

    [Header("Naturalidade da Fala")]
    [Range(0f, 300f)]
    public float sentencePauseMs = 85f;

    [Range(0f, 200f)]
    public float commaPauseMs = 25f;

    [Range(0f, 500f)]
    public float ellipsisPauseMs = 300f;

    [Range(0f, 250f)]
    public float questionPauseMs = 95f;

    public bool naturalSpeech = true;

    [Header("Naturalidade V3.1 - Prosódia Estável")]
    public bool dynamicProsody = false;

    [Range(0f, 5f)]
    public float prosodyVariation = 1.0f;

    [Range(0f, 8f)]
    public float emphasisStrength = 0.5f;

    [Range(0f, 6f)]
    public float questionIntonation = 0.5f;

    [Range(0f, 6f)]
    public float exclamationIntonation = 0.5f;

    [Range(0f, 6f)]
    public float reflectiveIntonation = 0.5f;

    [Range(0f, 100f)]
    public float streamingMergeDelayMs = 28f;

    public bool mergeStreamingPhrases = true;


    // ============================================================
    // CARREGAR CHAVE DO AZURE
    // ============================================================

    private bool TryLoadSpeechKey()
    {
        if (!string.IsNullOrWhiteSpace(speechKey))
            return true;

        speechKey =
            Environment.GetEnvironmentVariable(
                "AZURE_SPEECH_KEY"
            );

        if (string.IsNullOrWhiteSpace(speechKey))
        {
            Debug.LogError(
                "[NYRA TTS] Variável de ambiente AZURE_SPEECH_KEY não configurada."
            );

            return false;
        }

        return true;
    }


    // ============================================================
    // STREAMING TTS
    // ============================================================

    private readonly Queue<TTSItem> ttsTextQueue =
        new Queue<TTSItem>();

    private readonly Queue<AudioClip> audioQueue =
        new Queue<AudioClip>();

    private readonly object ttsLock =
        new object();

    private bool streamingActive = false;
    private bool streamingEnded = false;

    private bool synthesizing = false;
    private bool playing = false;

    private Coroutine synthesisCoroutine;
    private Coroutine playbackCoroutine;


    // ============================================================
    // ESTADO ATUAL DO STREAMING
    // ============================================================
    // Usado quando EnqueuePhrase recebe apenas o texto.
    //
    // Exemplo:
    //
    // BeginStreaming("happy", "greeting", 0.8f);
    //
    // EnqueuePhrase("Oi, que bom ver você!");
    //
    // A frase herdará automaticamente:
    //
    // emoção   = happy
    // intenção = greeting
    // intensidade = 0.8
    //
    // ============================================================

    private string streamingEmotion =
        "neutral";

    private string streamingIntent =
        "assertion";

    private float streamingIntensity =
        0.35f;


    // ============================================================
    // TTS ITEM
    // ============================================================

    private class TTSItem
    {
        public string text;

        public string emotion;

        public string intent;

        public float intensity;


        public TTSItem(
            string text,
            string emotion,
            string intent,
            float intensity
        )
        {
            this.text = text;

            this.emotion = emotion;

            this.intent = intent;

            this.intensity =
                Mathf.Clamp01(
                    intensity
                );
        }
    }


    // ============================================================
    // COMPATIBILIDADE - SPEAK NORMAL
    // ============================================================

    public IEnumerator Speak(
        string text,
        string emocao = "neutral"
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;


        if (audioSource == null)
        {
            Debug.LogError(
                "[NYRA TTS] AudioSource não configurado."
            );

            yield break;
        }


        audioSource.loop = false;


        if (!TryLoadSpeechKey())
            yield break;


        if (string.IsNullOrWhiteSpace(speechRegion))
        {
            Debug.LogError(
                "[NYRA TTS] Speech Region está vazia."
            );

            yield break;
        }


        StopStreaming();


        audioSource.Stop();


        text =
            LimparTexto(
                text
            );


        if (string.IsNullOrWhiteSpace(text))
            yield break;


        string ssml =
            GerarSSML(
                text,
                emocao,
                "assertion",
                0.35f
            );


        Debug.Log(
            "[NYRA TTS] Enviando fala..."
        );


        Debug.Log(
            "[NYRA TTS] Região: " +
            speechRegion
        );


        Debug.Log(
            "[NYRA TTS] Voz: " +
            voiceName
        );


        Debug.Log(
            "[NYRA TTS] Idioma: pt-BR"
        );


        Debug.Log(
            "[NYRA TTS] Emoção: " +
            emocao
        );


        Debug.Log(
            "[NYRA TTS] Intenção: assertion"
        );


        Debug.Log(
            "[NYRA TTS] Intensidade: 0.35"
        );


        Debug.Log(
            "[NYRA TTS] Texto: " +
            text
        );


        yield return StartCoroutine(
            RequisitarAudio(
                ssml
            )
        );
    }


    // ============================================================
    // INICIAR STREAMING
    // ============================================================

    // Mantém compatibilidade com:
    //
    // BeginStreaming()
    //
    // BeginStreaming("happy")
    //
    // BeginStreaming("happy", "greeting", 0.8f)
    //
    // ============================================================

    public void BeginStreaming(
        string emocao = "neutral",
        string intencao = "assertion",
        float intensidade = 0.35f
    )
    {
        StopStreaming();


        if (audioSource == null)
        {
            Debug.LogError(
                "[NYRA TTS] AudioSource não configurado."
            );

            return;
        }


        if (!TryLoadSpeechKey())
            return;


        if (string.IsNullOrWhiteSpace(speechRegion))
        {
            Debug.LogError(
                "[NYRA TTS] Speech Region está vazia."
            );

            return;
        }


        if (string.IsNullOrWhiteSpace(emocao))
        {
            emocao =
                "neutral";
        }


        if (string.IsNullOrWhiteSpace(intencao))
        {
            intencao =
                "assertion";
        }


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        // --------------------------------------------------------
        // GUARDA O ESTADO DO STREAMING
        // --------------------------------------------------------

        streamingEmotion =
            emocao;

        streamingIntent =
            intencao;

        streamingIntensity =
            intensidade;


        audioSource.loop = false;

        audioSource.Stop();


        lock (ttsLock)
        {
            ttsTextQueue.Clear();

            audioQueue.Clear();


            streamingActive =
                true;


            streamingEnded =
                false;


            synthesizing =
                false;


            playing =
                false;
        }


        synthesisCoroutine =
            StartCoroutine(
                ProcessarFilaTTS()
            );


        playbackCoroutine =
            StartCoroutine(
                ReproduzirFilaAudio()
            );


        Debug.Log(
            "[NYRA TTS] Streaming iniciado." +
            " | emoção=" +
            emocao +
            " | intenção=" +
            intencao +
            " | intensidade=" +
            intensidade.ToString("0.00")
        );
    }


    // ============================================================
    // ENFILEIRAR FRASE
    // ============================================================

    public void EnqueuePhrase(
        string text,
        string emocao = null,
        string intencao = null,
        float intensidade = -1f
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return;


        string limpo =
            LimparTexto(
                text
            );


        if (string.IsNullOrWhiteSpace(limpo))
            return;


        // --------------------------------------------------------
        // SE NÃO FOR INFORMADO, HERDA DO BEGINSTREAMING
        // --------------------------------------------------------

        if (string.IsNullOrWhiteSpace(emocao))
        {
            emocao =
                streamingEmotion;
        }


        if (string.IsNullOrWhiteSpace(emocao))
        {
            emocao =
                "neutral";
        }


        if (string.IsNullOrWhiteSpace(intencao))
        {
            intencao =
                streamingIntent;
        }


        if (string.IsNullOrWhiteSpace(intencao))
        {
            intencao =
                "assertion";
        }


        if (intensidade < 0f)
        {
            intensidade =
                streamingIntensity;
        }


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        lock (ttsLock)
        {
            if (!streamingActive)
            {
                Debug.LogWarning(
                    "[NYRA TTS] EnqueuePhrase ignorado: streaming não iniciado."
                );

                return;
            }


            ttsTextQueue.Enqueue(
                new TTSItem(
                    limpo,
                    emocao,
                    intencao,
                    intensidade
                )
            );
        }


        Debug.Log(
            "[NYRA TTS] Frase adicionada à fila: " +
            limpo +
            " | emoção=" +
            emocao +
            " | intenção=" +
            intencao +
            " | intensidade=" +
            intensidade.ToString("0.00")
        );
    }


    // ============================================================
    // FINALIZAR STREAMING
    // ============================================================

    public void EndStreaming()
    {
        lock (ttsLock)
        {
            streamingEnded =
                true;
        }


        Debug.Log(
            "[NYRA TTS] Fim do streaming recebido."
        );
    }


    // ============================================================
    // PARAR STREAMING
    // ============================================================

    public void StopStreaming()
    {
        streamingActive =
            false;


        streamingEnded =
            true;


        if (synthesisCoroutine != null)
        {
            StopCoroutine(
                synthesisCoroutine
            );

            synthesisCoroutine =
                null;
        }


        if (playbackCoroutine != null)
        {
            StopCoroutine(
                playbackCoroutine
            );

            playbackCoroutine =
                null;
        }


        lock (ttsLock)
        {
            while (
                audioQueue.Count > 0
            )
            {
                AudioClip clip =
                    audioQueue.Dequeue();


                if (clip != null)
                {
                    Destroy(
                        clip
                    );
                }
            }


            ttsTextQueue.Clear();


            synthesizing =
                false;


            playing =
                false;
        }
    }


    // ============================================================
    // STATUS
    // ============================================================

    public bool IsStreamingBusy
    {
        get
        {
            lock (ttsLock)
            {
                return
                    streamingActive ||
                    ttsTextQueue.Count > 0 ||
                    audioQueue.Count > 0 ||
                    synthesizing ||
                    playing;
            }
        }
    }


    // ============================================================
    // ESPERAR STREAMING TERMINAR
    // ============================================================

    public IEnumerator WaitForStreamingComplete()
    {
        while (true)
        {
            bool terminou;


            lock (ttsLock)
            {
                terminou =
                    streamingEnded &&
                    ttsTextQueue.Count == 0 &&
                    audioQueue.Count == 0 &&
                    !synthesizing &&
                    !playing;
            }


            if (terminou)
                break;


            yield return null;
        }


        streamingActive =
            false;


        Debug.Log(
            "[NYRA TTS] Streaming de voz completamente finalizado."
        );
    }


    // ============================================================
    // PROCESSADOR DE TEXTO → ÁUDIO
    // ============================================================

    private IEnumerator ProcessarFilaTTS()
    {
        while (true)
        {
            TTSItem item =
                null;


            lock (ttsLock)
            {
                if (
                    ttsTextQueue.Count > 0
                )
                {
                    item =
                        ttsTextQueue.Dequeue();


                    synthesizing =
                        true;
                }
                else
                {
                    synthesizing =
                        false;
                }
            }


            if (item != null)
            {
                // ====================================================
                // V3: TENTA AGRUPAR PEQUENOS PEDAÇOS DO STREAMING
                // ====================================================

                if (
                    mergeStreamingPhrases &&
                    streamingMergeDelayMs > 0f
                )
                {
                    yield return new WaitForSeconds(
                        streamingMergeDelayMs / 1000f
                    );


                    List<TTSItem> lote =
                        new List<TTSItem>();


                    lote.Add(
                        item
                    );


                    lock (ttsLock)
                    {
                        while (
                            ttsTextQueue.Count > 0 &&
                            lote.Count < 8
                        )
                        {
                            TTSItem proximo =
                                ttsTextQueue.Peek();


                            if (
                                DeveFinalizarLote(
                                    lote[
                                        lote.Count - 1
                                    ].text
                                )
                            )
                            {
                                break;
                            }


                            lote.Add(
                                ttsTextQueue.Dequeue()
                            );
                        }
                    }


                    if (lote.Count > 1)
                    {
                        item =
                            UnirItensTTS(
                                lote
                            );
                    }
                }


                yield return StartCoroutine(
                    SintetizarParaFila(
                        item
                    )
                );


                lock (ttsLock)
                {
                    synthesizing =
                        false;
                }


                continue;
            }


            bool deveEncerrar;


            lock (ttsLock)
            {
                deveEncerrar =
                    streamingEnded &&
                    ttsTextQueue.Count == 0;
            }


            if (deveEncerrar)
                break;


            yield return null;
        }


        lock (ttsLock)
        {
            synthesizing =
                false;
        }
    }


    // ============================================================
    // VERIFICA FINAL DE LOTE
    // ============================================================

    private bool DeveFinalizarLote(
        string texto
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;


        string t =
            texto.TrimEnd();


        if (t.Length == 0)
            return false;


        char ultimo =
            t[
                t.Length - 1
            ];


        return
            ultimo == '.' ||
            ultimo == '!' ||
            ultimo == '?' ||
            ultimo == '…';
    }


    // ============================================================
    // UNIR ITENS TTS
    // ============================================================

    private TTSItem UnirItensTTS(
        List<TTSItem> itens
    )
    {
        if (
            itens == null ||
            itens.Count == 0
        )
        {
            return null;
        }


        StringBuilder texto =
            new StringBuilder();


        string emocao =
            itens[0].emotion;


        string intencao =
            itens[0].intent;


        float intensidade =
            itens[0].intensity;


        for (
            int i = 0;
            i < itens.Count;
            i++
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    itens[i].text
                )
            )
            {
                continue;
            }


            if (texto.Length > 0)
            {
                texto.Append(
                    " "
                );
            }


            texto.Append(
                itens[i].text.Trim()
            );


            // ----------------------------------------------------
            // Mantém a lógica original da emoção:
            // uma emoção não-neutral posterior prevalece.
            // ----------------------------------------------------

            if (
                !string.IsNullOrWhiteSpace(
                    itens[i].emotion
                ) &&
                !itens[i].emotion.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                emocao =
                    itens[i].emotion;
            }


            // ----------------------------------------------------
            // Intenção mais recente.
            // ----------------------------------------------------

            if (
                !string.IsNullOrWhiteSpace(
                    itens[i].intent
                )
            )
            {
                intencao =
                    itens[i].intent;
            }


            // ----------------------------------------------------
            // Intensidade mais recente.
            // ----------------------------------------------------

            intensidade =
                itens[i].intensity;
        }


        return new TTSItem(
            texto.ToString().Trim(),
            string.IsNullOrWhiteSpace(emocao)
                ? "neutral"
                : emocao,
            string.IsNullOrWhiteSpace(intencao)
                ? "assertion"
                : intencao,
            Mathf.Clamp01(intensidade)
        );
    }


    // ============================================================
    // SINTETIZAR UMA FRASE
    // ============================================================

    private IEnumerator SintetizarParaFila(
        TTSItem item
    )
    {
        if (item == null)
            yield break;


        // ========================================================
        // V5:
        // emoção + intenção + intensidade
        // chegam até o GerarSSML.
        // ========================================================

        string ssml =
            GerarSSML(
                item.text,
                item.emotion,
                item.intent,
                item.intensity
            );


        string url =
            "https://" +
            speechRegion +
            ".tts.speech.microsoft.com/cognitiveservices/v1";


        byte[] body =
            Encoding.UTF8.GetBytes(
                ssml
            );


        using (
            UnityWebRequest request =
                new UnityWebRequest(
                    url,
                    "POST"
                )
        )
        {
            request.uploadHandler =
                new UploadHandlerRaw(
                    body
                );


            request.downloadHandler =
                new DownloadHandlerBuffer();


            request.SetRequestHeader(
                "Ocp-Apim-Subscription-Key",
                speechKey
            );


            request.SetRequestHeader(
                "Content-Type",
                "application/ssml+xml"
            );


            request.SetRequestHeader(
                "X-Microsoft-OutputFormat",
                "riff-24khz-16bit-mono-pcm"
            );


            request.SetRequestHeader(
                "User-Agent",
                "NyraUnityTTS"
            );


            yield return request.SendWebRequest();


            if (
                request.result !=
                UnityWebRequest.Result.Success
            )
            {
                Debug.LogError(
                    "[NYRA TTS] ERRO AZURE STREAM: " +
                    request.responseCode +
                    " - " +
                    request.error
                );


                yield break;
            }


            byte[] audioBytes =
                request.downloadHandler.data;


            if (
                audioBytes == null ||
                audioBytes.Length < 100
            )
            {
                Debug.LogError(
                    "[NYRA TTS] Azure retornou poucos dados."
                );


                yield break;
            }


            // ====================================================
            // AZURE ESTÁ USANDO:
            //
            // riff-24khz-16bit-mono-pcm
            //
            // Portanto sampleRate = 24000.
            // ====================================================

            AudioClip clip =
                WavUtility.ToAudioClip(
                    audioBytes,
                    "NyraVoice",
                    24000
                );


            if (clip == null)
            {
                Debug.LogError(
                    "[NYRA TTS] Não foi possível criar AudioClip."
                );


                yield break;
            }


            lock (ttsLock)
            {
                audioQueue.Enqueue(
                    clip
                );
            }


            Debug.Log(
                "[NYRA TTS] Áudio preparado: " +
                clip.length.ToString("0.00") +
                "s" +
                " | emoção=" +
                item.emotion +
                " | intenção=" +
                item.intent +
                " | intensidade=" +
                item.intensity.ToString("0.00")
            );
        }
    }


    // ============================================================
    // REPRODUZIR FILA
    // ============================================================

    private IEnumerator ReproduzirFilaAudio()
    {
        while (true)
        {
            AudioClip clip =
                null;


            lock (ttsLock)
            {
                if (
                    audioQueue.Count > 0
                )
                {
                    clip =
                        audioQueue.Dequeue();


                    playing =
                        true;
                }
                else
                {
                    playing =
                        false;
                }
            }


            if (clip != null)
            {
                if (audioSource != null)
                {
                    audioSource.clip =
                        clip;


                    audioSource.loop =
                        false;


                    audioSource.Play();


                    Debug.Log(
                        "[NYRA TTS] Reproduzindo frase."
                    );


                    while (
                        audioSource.isPlaying
                    )
                    {
                        yield return null;
                    }
                }


                if (clip != null)
                {
                    Destroy(
                        clip
                    );
                }


                lock (ttsLock)
                {
                    playing =
                        false;
                }


                continue;
            }


            bool terminou;


            lock (ttsLock)
            {
                terminou =
                    streamingEnded &&
                    audioQueue.Count == 0 &&
                    ttsTextQueue.Count == 0 &&
                    !synthesizing;
            }


            if (terminou)
                break;


            yield return null;
        }


        lock (ttsLock)
        {
            playing =
                false;


            streamingActive =
                false;
        }


        Debug.Log(
            "[NYRA TTS] Reprodução do streaming terminou."
        );
    }


    // ============================================================
    // SSML
    // ============================================================

    private string GerarSSML(
        string texto,
        string emocao,
        string intencao,
        float intensidade
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";


        if (string.IsNullOrWhiteSpace(emocao))
        {
            emocao =
                "neutral";
        }


        if (string.IsNullOrWhiteSpace(intencao))
        {
            intencao =
                "assertion";
        }


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        int ratePercent =
            Mathf.RoundToInt(
                (rate - 1.0f) * 100.0f
            );


        float ajusteRate =
            0f;


        float ajustePitch =
            0f;


        switch (
            (emocao ?? "neutral").ToLower()
        )
        {
            case "happy":

            case "feliz":

            case "alegre":

                ajusteRate =
                    1.5f;

                ajustePitch =
                    1.5f;

                break;


            case "excited":

            case "animada":

            case "empolgada":

                ajusteRate =
                    4f;

                ajustePitch =
                    3f;

                break;


            case "sad":

            case "triste":

                ajusteRate =
                    -4f;

                ajustePitch =
                    -3f;

                break;


            case "angry":

            case "raiva":

            case "irritada":

                ajusteRate =
                    2.5f;

                ajustePitch =
                    -1f;

                break;


            case "surprised":

            case "surpresa":

                ajusteRate =
                    3f;

                ajustePitch =
                    3f;

                break;


            case "soft":

            case "suave":

                ajusteRate =
                    -3f;

                ajustePitch =
                    1f;

                break;


            case "whisper":

            case "sussurro":

                ajusteRate =
                    -5f;

                ajustePitch =
                    0f;

                break;
        }


        int velocidadeFinal =
            Mathf.RoundToInt(
                ratePercent +
                ajusteRate
            );


        float pitchFinal =
            pitchPercent +
            ajustePitch;


        velocidadeFinal =
            Mathf.Clamp(
                velocidadeFinal,
                -50,
                100
            );


        pitchFinal =
            Mathf.Clamp(
                pitchFinal,
                -50,
                50
            );


        string rateAzure =
            FormatarPercentual(
                velocidadeFinal
            );


        string pitchAzure =
            FormatarPercentual(
                pitchFinal
            );


        StringBuilder ssml =
            new StringBuilder();


        ssml.Append(
            "<speak version=\"1.0\" " +
            "xmlns=\"http://www.w3.org/2001/10/synthesis\" " +
            "xmlns:mstts=\"http://www.w3.org/2001/mstts\" " +
            "xml:lang=\"pt-BR\">"
        );


        ssml.Append(
            "<voice name=\"" +
            EscapeXml(voiceName) +
            "\">"
        );


        if (useGirlRole)
        {
            ssml.Append(
                "<mstts:express-as role=\"Girl\">"
            );
        }


        if (dynamicProsody)
        {
            ssml.Append(
                GerarProsodiaDinamica(
                    texto,
                    velocidadeFinal,
                    pitchFinal,
                    intencao,
                    intensidade
                )
            );
        }
        else
        {
            string textoNatural =
                naturalSpeech
                    ? AdicionarPausasNaturais(texto)
                    : EscapeXml(texto);


            // ----------------------------------------------------
            // Mesmo com dynamicProsody desligado,
            // intenção e intensidade continuam podendo modificar
            // a prosódia geral.
            // ----------------------------------------------------

            int rateV5 =
                CalcularRatePorIntencao(
                    velocidadeFinal,
                    intencao,
                    intensidade
                );


            float pitchV5 =
                CalcularPitchPorIntencao(
                    pitchFinal,
                    intencao,
                    intensidade
                );


            ssml.Append(
                "<prosody rate=\"" +
                FormatarPercentual(rateV5) +
                "\" pitch=\"" +
                FormatarPercentual(pitchV5) +
                "\">"
            );


            ssml.Append(
                textoNatural
            );


            ssml.Append(
                "</prosody>"
            );
        }


        if (useGirlRole)
        {
            ssml.Append(
                "</mstts:express-as>"
            );
        }


        ssml.Append(
            "</voice></speak>"
        );


        return ssml.ToString();
    }


    // ============================================================
    // PROSÓDIA DINÂMICA V5
    // ============================================================

    private string GerarProsodiaDinamica(
        string texto,
        int rateBase,
        float pitchBase,
        string intencao,
        float intensidade
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";


        if (string.IsNullOrWhiteSpace(intencao))
        {
            intencao =
                "assertion";
        }


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        List<string> unidades =
            DividirEmUnidadesDeFala(
                texto
            );


        if (
            unidades == null ||
            unidades.Count == 0
        )
        {
            unidades =
                new List<string>();


            unidades.Add(
                texto
            );
        }


        StringBuilder resultado =
            new StringBuilder();


        // ========================================================
        // V5:
        //
        // A intenção define a direção da fala.
        //
        // A intensidade define o quanto essa direção aparece.
        //
        // Emoção já chegou em rateBase/pitchBase.
        // ========================================================

        int ajusteRateIntencao =
            0;


        float ajustePitchIntencao =
            0f;


        switch (
            intencao.Trim().ToLower()
        )
        {
            case "question":

            case "pergunta":

            case "questioning":

            case "curiosity":

            case "curiosidade":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                ajustePitchIntencao =
                    3.5f *
                    intensidade;

                break;


            case "greeting":

            case "saudacao":

            case "saudação":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        1f *
                        intensidade
                    );

                ajustePitchIntencao =
                    1.5f *
                    intensidade;

                break;


            case "excited":

            case "excitement":

            case "entusiasmo":

            case "empolgado":

            case "empolgada":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        3f *
                        intensidade
                    );

                ajustePitchIntencao =
                    3.5f *
                    intensidade;

                break;


            case "emphasis":

            case "emphasis_high":

            case "ênfase":

            case "enfase":

            case "important":

            case "importante":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                ajustePitchIntencao =
                    2f *
                    intensidade;

                break;


            case "comfort":

            case "conforto":

            case "comforting":

            case "acolhimento":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                ajustePitchIntencao =
                    -1.5f *
                    intensidade;

                break;


            case "sad":

            case "triste":

            case "melancholy":

            case "melancolia":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        -3f *
                        intensidade
                    );

                ajustePitchIntencao =
                    -2.5f *
                    intensidade;

                break;


            case "angry":

            case "raiva":

            case "irritado":

            case "irritada":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        2f *
                        intensidade
                    );

                ajustePitchIntencao =
                    -0.5f *
                    intensidade;

                break;


            case "surprise":

            case "surpresa":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        2f *
                        intensidade
                    );

                ajustePitchIntencao =
                    4f *
                    intensidade;

                break;


            case "whisper":

            case "sussurro":

                ajusteRateIntencao =
                    Mathf.RoundToInt(
                        -3f *
                        intensidade
                    );

                ajustePitchIntencao =
                    0.5f *
                    intensidade;

                break;


            case "assertion":

            case "afirmacao":

            case "afirmação":

            default:

                ajusteRateIntencao =
                    0;

                ajustePitchIntencao =
                    0f;

                break;
        }


        int rateV5 =
            Mathf.Clamp(
                rateBase +
                ajusteRateIntencao,
                -50,
                100
            );


        float pitchV5 =
            Mathf.Clamp(
                pitchBase +
                ajustePitchIntencao,
                -50f,
                50f
            );


        for (
            int i = 0;
            i < unidades.Count;
            i++
        )
        {
            string unidade =
                unidades[i];


            if (
                string.IsNullOrWhiteSpace(
                    unidade
                )
            )
            {
                continue;
            }


            string limpa =
                unidade.Trim();


            bool pergunta =
                limpa.EndsWith("?");


            bool exclamacao =
                limpa.EndsWith("!");


            bool reticencias =
                limpa.EndsWith("...") ||
                limpa.EndsWith("…");


            int variacaoRate =
                CalcularVariacaoVelocidade(
                    limpa,
                    i
                );


            float variacaoPitch =
                CalcularVariacaoNatural(
                    i,
                    limpa
                );


            int rateUnidade =
                Mathf.Clamp(
                    rateV5 +
                    variacaoRate,
                    -50,
                    100
                );


            float pitchUnidade =
                Mathf.Clamp(
                    pitchV5 +
                    variacaoPitch,
                    -50f,
                    50f
                );


            resultado.Append(
                GerarUnidadeComProsodia(
                    limpa,
                    rateUnidade,
                    pitchUnidade,
                    pergunta,
                    exclamacao,
                    reticencias,
                    intencao,
                    intensidade
                )
            );


            resultado.Append(
                GerarPausaDaUnidade(
                    limpa
                )
            );
        }


        return resultado.ToString();
    }


    // ============================================================
    // CALCULAR RATE POR INTENÇÃO
    // ============================================================

    private int CalcularRatePorIntencao(
        int rateBase,
        string intencao,
        float intensidade
    )
    {
        if (string.IsNullOrWhiteSpace(intencao))
            return rateBase;


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        int ajuste =
            0;


        switch (
            intencao.Trim().ToLower()
        )
        {
            case "question":
            case "pergunta":
            case "curiosity":
            case "curiosidade":

                ajuste =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                break;


            case "greeting":
            case "saudacao":
            case "saudação":

                ajuste =
                    Mathf.RoundToInt(
                        1f *
                        intensidade
                    );

                break;


            case "excited":
            case "entusiasmo":
            case "empolgado":
            case "empolgada":

                ajuste =
                    Mathf.RoundToInt(
                        3f *
                        intensidade
                    );

                break;


            case "emphasis":
            case "ênfase":
            case "enfase":
            case "important":
            case "importante":

                ajuste =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                break;


            case "comfort":
            case "conforto":
            case "comforting":
            case "acolhimento":

                ajuste =
                    Mathf.RoundToInt(
                        -2f *
                        intensidade
                    );

                break;


            case "sad":
            case "triste":
            case "melancholy":
            case "melancolia":

                ajuste =
                    Mathf.RoundToInt(
                        -3f *
                        intensidade
                    );

                break;


            case "angry":
            case "raiva":
            case "irritado":
            case "irritada":

                ajuste =
                    Mathf.RoundToInt(
                        2f *
                        intensidade
                    );

                break;


            case "surprise":
            case "surpresa":

                ajuste =
                    Mathf.RoundToInt(
                        2f *
                        intensidade
                    );

                break;


            case "whisper":
            case "sussurro":

                ajuste =
                    Mathf.RoundToInt(
                        -3f *
                        intensidade
                    );

                break;
        }


        return
            Mathf.Clamp(
                rateBase + ajuste,
                -50,
                100
            );
    }


    // ============================================================
    // CALCULAR PITCH POR INTENÇÃO
    // ============================================================

    private float CalcularPitchPorIntencao(
        float pitchBase,
        string intencao,
        float intensidade
    )
    {
        if (string.IsNullOrWhiteSpace(intencao))
            return pitchBase;


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        float ajuste =
            0f;


        switch (
            intencao.Trim().ToLower()
        )
        {
            case "question":
            case "pergunta":
            case "questioning":
            case "curiosity":
            case "curiosidade":

                ajuste =
                    3.5f *
                    intensidade;

                break;


            case "greeting":
            case "saudacao":
            case "saudação":

                ajuste =
                    1.5f *
                    intensidade;

                break;


            case "excited":
            case "excitement":
            case "entusiasmo":
            case "empolgado":
            case "empolgada":

                ajuste =
                    3.5f *
                    intensidade;

                break;


            case "emphasis":
            case "ênfase":
            case "enfase":
            case "important":
            case "importante":

                ajuste =
                    2f *
                    intensidade;

                break;


            case "comfort":
            case "conforto":
            case "comforting":
            case "acolhimento":

                ajuste =
                    -1.5f *
                    intensidade;

                break;


            case "sad":
            case "triste":
            case "melancholy":
            case "melancolia":

                ajuste =
                    -2.5f *
                    intensidade;

                break;


            case "angry":
            case "raiva":
            case "irritado":
            case "irritada":

                ajuste =
                    -0.5f *
                    intensidade;

                break;


            case "surprise":
            case "surpresa":

                ajuste =
                    4f *
                    intensidade;

                break;


            case "whisper":
            case "sussurro":

                ajuste =
                    0.5f *
                    intensidade;

                break;
        }


        return
            Mathf.Clamp(
                pitchBase + ajuste,
                -50f,
                50f
            );
    }


    // ============================================================
    // DIVIDIR EM UNIDADES DE FALA
    // ============================================================

    private List<string> DividirEmUnidadesDeFala(
        string texto
    )
    {
        List<string> unidades =
            new List<string>();


        if (string.IsNullOrWhiteSpace(texto))
            return unidades;


        StringBuilder atual =
            new StringBuilder();


        for (
            int i = 0;
            i < texto.Length;
            i++
        )
        {
            char c =
                texto[i];


            atual.Append(
                c
            );


            if (
                c == '.' &&
                i + 2 < texto.Length &&
                texto[i + 1] == '.' &&
                texto[i + 2] == '.'
            )
            {
                atual.Append(
                    '.'
                );


                atual.Append(
                    '.'
                );


                i += 2;


                AdicionarUnidade(
                    unidades,
                    atual
                );


                atual.Clear();


                continue;
            }


            if (
                c == '.' ||
                c == '!' ||
                c == '?' ||
                c == '…'
            )
            {
                if (
                    c == '.' &&
                    i > 0 &&
                    i + 1 < texto.Length &&
                    char.IsDigit(texto[i - 1]) &&
                    char.IsDigit(texto[i + 1])
                )
                {
                    continue;
                }


                AdicionarUnidade(
                    unidades,
                    atual
                );


                atual.Clear();


                continue;
            }


            if (
                (c == ',' ||
                 c == ';' ||
                 c == ':') &&
                ContarPalavras(
                    atual.ToString()
                ) >= 4
            )
            {
                AdicionarUnidade(
                    unidades,
                    atual
                );


                atual.Clear();
            }
        }


        if (atual.Length > 0)
        {
            AdicionarUnidade(
                unidades,
                atual
            );
        }


        return unidades;
    }


    // ============================================================
    // ADICIONAR UNIDADE
    // ============================================================

    private void AdicionarUnidade(
        List<string> unidades,
        StringBuilder texto
    )
    {
        if (texto == null)
            return;


        string valor =
            texto.ToString().Trim();


        if (!string.IsNullOrWhiteSpace(valor))
        {
            unidades.Add(
                valor
            );
        }
    }


    // ============================================================
    // GERAR UNIDADE COM PROSÓDIA V5
    // ============================================================

    private string GerarUnidadeComProsodia(
        string texto,
        int rateBase,
        float pitchBase,
        bool pergunta,
        bool exclamacao,
        bool reticencias,
        string intencao,
        float intensidade
    )
    {
        List<string> palavras =
            ExtrairTokens(
                texto
            );


        if (palavras.Count == 0)
            return "";


        intensidade =
            Mathf.Clamp01(
                intensidade
            );


        float bonusIntencao =
            0f;


        switch (
            (intencao ?? "assertion").ToLower()
        )
        {
            case "question":

            case "pergunta":

            case "questioning":

            case "curiosity":

            case "curiosidade":

                bonusIntencao =
                    1.5f *
                    intensidade;

                break;


            case "emphasis":

            case "emphasis_high":

            case "ênfase":

            case "enfase":

            case "important":

            case "importante":

                bonusIntencao =
                    2.5f *
                    intensidade;

                break;


            case "excited":

            case "excitement":

            case "entusiasmo":

            case "empolgada":

            case "empolgado":

                bonusIntencao =
                    2f *
                    intensidade;

                break;


            case "surprise":

            case "surpresa":

                bonusIntencao =
                    3f *
                    intensidade;

                break;
        }


        // ========================================================
        // Intensidade também controla a força da ênfase.
        // ========================================================

        float intensidadeEnfase =
            emphasisStrength *
            intensidade;


        // ========================================================
        // PASSAGEM PRINCIPAL
        // ========================================================

        StringBuilder resultado =
            new StringBuilder();


        for (
            int i = 0;
            i < palavras.Count;
            i++
        )
        {
            string token =
                palavras[i];


            if (string.IsNullOrWhiteSpace(token))
                continue;


            bool pontuacao =
                EhPontuacao(
                    token
                );


            if (pontuacao)
            {
                resultado.Append(
                    EscapeXml(token)
                );


                continue;
            }


            bool enfatizar =
                DeveEnfatizarPalavra(
                    token,
                    texto,
                    i,
                    palavras.Count,
                    pergunta,
                    exclamacao
                );


            // ----------------------------------------------------
            // V5: intensidade reforça apenas quando a palavra
            // já foi identificada como uma palavra importante.
            // ----------------------------------------------------

            if (!enfatizar)
            {
                resultado.Append(
                    EscapeXml(token)
                );
            }
            else
            {
                float pitchPalavra =
                    pitchBase +
                    intensidadeEnfase +
                    bonusIntencao;


                // Perguntas ganham leve elevação nas palavras
                // finais.
                if (
                    pergunta &&
                    i >= palavras.Count - 3
                )
                {
                    pitchPalavra +=
                        questionIntonation *
                        intensidade;
                }


                // Exclamações ganham um reforço adicional.
                if (
                    exclamacao &&
                    i == palavras.Count - 1
                )
                {
                    pitchPalavra +=
                        exclamationIntonation *
                        intensidade;
                }


                // Reticências tendem a ficar mais reflexivas.
                if (reticencias)
                {
                    pitchPalavra -=
                        reflectiveIntonation *
                        intensidade;
                }


                int ratePalavra =
                    rateBase -
                    Mathf.RoundToInt(
                        2f *
                        intensidade
                    );


                resultado.Append(
                    "<prosody rate=\"" +
                    FormatarPercentual(
                        Mathf.Clamp(
                            ratePalavra,
                            -50,
                            100
                        )
                    ) +
                    "\" pitch=\"" +
                    FormatarPercentual(
                        Mathf.Clamp(
                            pitchPalavra,
                            -50f,
                            50f
                        )
                    ) +
                    "\">"
                );


                resultado.Append(
                    EscapeXml(token)
                );


                resultado.Append(
                    "</prosody>"
                );
            }


            if (
                i < palavras.Count - 1
            )
            {
                bool proximoEhPontuacao =
                    EhPontuacao(
                        palavras[i + 1]
                    );


                if (
                    !proximoEhPontuacao ||
                    pontuacao
                )
                {
                    resultado.Append(
                        " "
                    );
                }
            }
        }


        resultado.Insert(
            0,
            "<prosody rate=\"" +
            FormatarPercentual(rateBase) +
            "\" pitch=\"" +
            FormatarPercentual(pitchBase) +
            "\">"
        );


        resultado.Append(
            "</prosody>"
        );


        return resultado.ToString();
    }


    // ============================================================
    // EXTRAIR TOKENS
    // ============================================================

    private List<string> ExtrairTokens(
        string texto
    )
    {
        List<string> tokens =
            new List<string>();


        StringBuilder palavra =
            new StringBuilder();


        for (
            int i = 0;
            i < texto.Length;
            i++
        )
        {
            char c =
                texto[i];


            if (char.IsWhiteSpace(c))
            {
                if (palavra.Length > 0)
                {
                    tokens.Add(
                        palavra.ToString()
                    );


                    palavra.Clear();
                }


                continue;
            }


            if (
                char.IsPunctuation(c) &&
                c != '-' &&
                c != '\''
            )
            {
                if (palavra.Length > 0)
                {
                    tokens.Add(
                        palavra.ToString()
                    );


                    palavra.Clear();
                }


                if (
                    c == '.' &&
                    i + 2 < texto.Length &&
                    texto[i + 1] == '.' &&
                    texto[i + 2] == '.'
                )
                {
                    tokens.Add(
                        "..."
                    );


                    i += 2;
                }
                else
                {
                    tokens.Add(
                        c.ToString()
                    );
                }


                continue;
            }


            palavra.Append(
                c
            );
        }


        if (palavra.Length > 0)
        {
            tokens.Add(
                palavra.ToString()
            );
        }


        return tokens;
    }


    // ============================================================
    // É PONTUAÇÃO
    // ============================================================

    private bool EhPontuacao(
        string token
    )
    {
        if (string.IsNullOrEmpty(token))
            return false;


        for (
            int i = 0;
            i < token.Length;
            i++
        )
        {
            if (!char.IsPunctuation(token[i]))
                return false;
        }


        return true;
    }


    // ============================================================
    // DEVE ENFATIZAR
    // ============================================================

    private bool DeveEnfatizarPalavra(
        string palavra,
        string frase,
        int indice,
        int total,
        bool pergunta,
        bool exclamacao
    )
    {
        if (string.IsNullOrWhiteSpace(palavra))
            return false;


        string p =
            palavra
                .Trim(
                    '(', ')', '[', ']',
                    '{', '}', ',', '.', '!',
                    '?', ':', ';', '"', '\''
                )
                .ToLower();


        if (p.Length < 3)
            return false;


        if (
            indice > 0 &&
            total > 12 &&
            indice % 9 != 0
        )
        {
        }


        string[] fortes =
        {
            "muito",
            "muita",
            "muitos",
            "muitas",
            "realmente",
            "mesmo",
            "mesma",
            "nunca",
            "sempre",
            "agora",
            "aqui",
            "ali",
            "sério",
            "serio",
            "claro",
            "claro!",
            "exatamente",
            "incrível",
            "incrivel",
            "nossa",
            "uau",
            "caramba",
            "finalmente",
            "importante",
            "verdade",
            "verdadeiro",
            "verdadeira",
            "porquê",
            "porque"
        };


        for (
            int i = 0;
            i < fortes.Length;
            i++
        )
        {
            if (
                p == fortes[i]
            )
            {
                return true;
            }
        }


        if (
            pergunta &&
            indice >= total - 3 &&
            total >= 5
        )
        {
            return true;
        }


        if (
            exclamacao &&
            indice == total - 1 &&
            total >= 4
        )
        {
            return true;
        }


        return false;
    }


    // ============================================================
    // VARIAÇÃO NATURAL
    // ============================================================

    private float CalcularVariacaoNatural(
        int indice,
        string unidade
    )
    {
        if (prosodyVariation <= 0f)
            return 0f;


        float[] padrao =
        {
            0f,
            0.6f,
            -0.4f,
            0.8f,
            -0.6f,
            0.3f
        };


        float valor =
            padrao[
                indice % padrao.Length
            ];


        return valor *
               prosodyVariation;
    }


    // ============================================================
    // VARIAÇÃO DE VELOCIDADE
    // ============================================================

    private int CalcularVariacaoVelocidade(
        string unidade,
        int indice
    )
    {
        int palavras =
            ContarPalavras(
                unidade
            );


        int ajuste =
            0;


        if (palavras <= 3)
        {
            ajuste =
                -1;
        }
        else if (palavras >= 16)
        {
            ajuste =
                1;
        }


        if (indice % 4 == 3)
        {
            ajuste -= 1;
        }


        return ajuste;
    }


    // ============================================================
    // PAUSA DA UNIDADE
    // ============================================================

    private string GerarPausaDaUnidade(
        string unidade
    )
    {
        string t =
            unidade.TrimEnd();


        if (
            t.EndsWith("...") ||
            t.EndsWith("…")
        )
        {
            return
                "<break time=\"" +
                Mathf.RoundToInt(
                    ellipsisPauseMs
                ) +
                "ms\"/>";
        }


        if (t.EndsWith("?"))
        {
            return
                "<break time=\"" +
                Mathf.RoundToInt(
                    questionPauseMs
                ) +
                "ms\"/>";
        }


        if (
            t.EndsWith("!") ||
            t.EndsWith(".")
        )
        {
            return
                "<break time=\"" +
                Mathf.RoundToInt(
                    sentencePauseMs
                ) +
                "ms\"/>";
        }


        if (t.EndsWith(","))
        {
            int palavras =
                ContarPalavras(
                    t
                );


            float pausa =
                palavras >= 12
                    ? commaPauseMs
                    : commaPauseMs * 0.45f;


            return
                "<break time=\"" +
                Mathf.RoundToInt(
                    pausa
                ) +
                "ms\"/>";
        }


        if (t.EndsWith(";"))
        {
            return
                "<break time=\"70ms\"/>";
        }


        if (t.EndsWith(":"))
        {
            return
                "<break time=\"60ms\"/>";
        }


        return "";
    }


    // ============================================================
    // CONTAR PALAVRAS
    // ============================================================

    private int ContarPalavras(
        string texto
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return 0;


        string[] partes =
            texto.Split(
                new char[]
                {
                    ' ',
                    '\t',
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries
            );


        int total =
            0;


        for (
            int i = 0;
            i < partes.Length;
            i++
        )
        {
            string p =
                partes[i].Trim(
                    ',',
                    '.',
                    '!',
                    '?',
                    ';',
                    ':',
                    '(',
                    ')',
                    '[',
                    ']',
                    '"',
                    '\''
                );


            if (
                !string.IsNullOrWhiteSpace(p)
            )
            {
                total++;
            }
        }


        return total;
    }


    // ============================================================
    // FORMATAR PERCENTUAL
    // ============================================================

    private string FormatarPercentual(
        float valor
    )
    {
        int arredondado =
            Mathf.RoundToInt(
                valor
            );


        return
            (arredondado >= 0 ? "+" : "") +
            arredondado +
            "%";
    }


    // ============================================================
    // ESCAPE XML
    // ============================================================

    private string EscapeXml(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
            return "";


        return texto
            .Replace(
                "&",
                "&amp;"
            )
            .Replace(
                "<",
                "&lt;"
            )
            .Replace(
                ">",
                "&gt;"
            )
            .Replace(
                "\"",
                "&quot;"
            )
            .Replace(
                "'",
                "&apos;"
            );
    }


    // ============================================================
    // PAUSAS NATURAIS - FALLBACK
    // ============================================================

    private string AdicionarPausasNaturais(
        string texto
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";


        StringBuilder resultado =
            new StringBuilder();


        for (
            int i = 0;
            i < texto.Length;
            i++
        )
        {
            char atual =
                texto[i];


            if (
                atual == '.' &&
                i + 2 < texto.Length &&
                texto[i + 1] == '.' &&
                texto[i + 2] == '.'
            )
            {
                resultado.Append(
                    "..."
                );


                resultado.Append(
                    "<break time=\"" +
                    Mathf.RoundToInt(
                        ellipsisPauseMs
                    ) +
                    "ms\"/>"
                );


                i += 2;

                continue;
            }


            if (atual == '.')
            {
                bool decimalPoint =
                    i > 0 &&
                    i + 1 < texto.Length &&
                    char.IsDigit(
                        texto[i - 1]
                    ) &&
                    char.IsDigit(
                        texto[i + 1]
                    );


                resultado.Append(
                    "."
                );


                if (
                    !decimalPoint &&
                    ExisteTextoDepois(
                        texto,
                        i + 1
                    )
                )
                {
                    resultado.Append(
                        "<break time=\"" +
                        Mathf.RoundToInt(
                            sentencePauseMs
                        ) +
                        "ms\"/>"
                    );
                }


                continue;
            }


            if (atual == '!')
            {
                resultado.Append(
                    "!"
                );


                if (
                    ExisteTextoDepois(
                        texto,
                        i + 1
                    )
                )
                {
                    resultado.Append(
                        "<break time=\"" +
                        Mathf.RoundToInt(
                            sentencePauseMs
                        ) +
                        "ms\"/>"
                    );
                }


                continue;
            }


            if (atual == '?')
            {
                resultado.Append(
                    "?"
                );


                if (
                    ExisteTextoDepois(
                        texto,
                        i + 1
                    )
                )
                {
                    resultado.Append(
                        "<break time=\"" +
                        Mathf.RoundToInt(
                            questionPauseMs
                        ) +
                        "ms\"/>"
                    );
                }


                continue;
            }


            if (atual == ',')
            {
                resultado.Append(
                    ","
                );


                if (
                    DeveAdicionarPausaDeVirgula(
                        texto,
                        i
                    )
                )
                {
                    resultado.Append(
                        "<break time=\"" +
                        Mathf.RoundToInt(
                            commaPauseMs
                        ) +
                        "ms\"/>"
                    );
                }


                continue;
            }


            if (atual == ';')
            {
                resultado.Append(
                    ";"
                );


                resultado.Append(
                    "<break time=\"70ms\"/>"
                );


                continue;
            }


            if (atual == ':')
            {
                resultado.Append(
                    ":"
                );


                resultado.Append(
                    "<break time=\"60ms\"/>"
                );


                continue;
            }


            resultado.Append(
                EscapeXml(
                    atual.ToString()
                )
            );
        }


        return resultado.ToString();
    }


    // ============================================================
    // EXISTE TEXTO DEPOIS
    // ============================================================

    private bool ExisteTextoDepois(
        string texto,
        int indice
    )
    {
        for (
            int i = indice;
            i < texto.Length;
            i++
        )
        {
            if (!char.IsWhiteSpace(texto[i]))
                return true;
        }


        return false;
    }


    // ============================================================
    // PAUSA DE VÍRGULA
    // ============================================================

    private bool DeveAdicionarPausaDeVirgula(
        string texto,
        int indice
    )
    {
        if (
            indice > 0 &&
            indice + 1 < texto.Length &&
            char.IsDigit(
                texto[indice - 1]
            ) &&
            char.IsDigit(
                texto[indice + 1]
            )
        )
        {
            return false;
        }


        int palavras =
            ContarPalavras(
                texto.Substring(
                    0,
                    indice
                )
            );


        if (palavras >= 8)
            return true;


        string depois =
            texto.Substring(
                indice + 1
            ).TrimStart();


        string[] marcadores =
        {
            "mas ",
            "porém ",
            "porem ",
            "então ",
            "entao ",
            "porque ",
            "só que ",
            "so que ",
            "na verdade ",
            "ou seja "
        };


        for (
            int i = 0;
            i < marcadores.Length;
            i++
        )
        {
            if (
                depois.StartsWith(
                    marcadores[i],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }
        }


        return false;
    }


    // ============================================================
    // REQUEST NORMAL
    // ============================================================

    private IEnumerator RequisitarAudio(
        string ssml
    )
    {
        string url =
            "https://" +
            speechRegion +
            ".tts.speech.microsoft.com/cognitiveservices/v1";


        byte[] body =
            Encoding.UTF8.GetBytes(
                ssml
            );


        using (
            UnityWebRequest request =
                new UnityWebRequest(
                    url,
                    "POST"
                )
        )
        {
            request.uploadHandler =
                new UploadHandlerRaw(
                    body
                );


            request.downloadHandler =
                new DownloadHandlerBuffer();


            request.SetRequestHeader(
                "Ocp-Apim-Subscription-Key",
                speechKey
            );


            request.SetRequestHeader(
                "Content-Type",
                "application/ssml+xml"
            );


            request.SetRequestHeader(
                "X-Microsoft-OutputFormat",
                "riff-24khz-16bit-mono-pcm"
            );


            request.SetRequestHeader(
                "User-Agent",
                "NyraUnityTTS"
            );


            yield return request.SendWebRequest();


            if (
                request.result !=
                UnityWebRequest.Result.Success
            )
            {
                Debug.LogError(
                    "[NYRA TTS] ERRO AZURE: " +
                    request.responseCode +
                    " - " +
                    request.error
                );


                yield break;
            }


            byte[] audioBytes =
                request.downloadHandler.data;


            if (
                audioBytes == null ||
                audioBytes.Length < 100
            )
            {
                Debug.LogError(
                    "[NYRA TTS] Azure retornou poucos dados de áudio."
                );


                yield break;
            }


            // ====================================================
            // CORREÇÃO CS7036
            //
            // Mantido exatamente como solicitado:
            //
            // WavUtility(..., "NyraVoice", 24000)
            // ====================================================

            AudioClip clip =
                WavUtility.ToAudioClip(
                    audioBytes,
                    "NyraVoice",
                    24000
                );


            if (clip == null)
            {
                Debug.LogError(
                    "[NYRA TTS] Não foi possível criar o AudioClip."
                );


                yield break;
            }


            audioSource.clip =
                clip;


            audioSource.loop =
                false;


            audioSource.Play();


            Debug.Log(
                "[NYRA TTS] Nyra falando. Duração: " +
                clip.length +
                " segundos."
            );


            while (
                audioSource.isPlaying
            )
            {
                yield return null;
            }


            Destroy(
                clip
            );
        }
    }


    // ============================================================
    // LIMPEZA
    // ============================================================

    private string LimparTexto(
        string texto
    )
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";


        StringBuilder sb =
            new StringBuilder();


        foreach (char c in texto)
        {
            if (
                char.IsLetterOrDigit(c) ||
                char.IsWhiteSpace(c) ||
                char.IsPunctuation(c)
            )
            {
                sb.Append(
                    c
                );
            }
        }


        return sb
            .ToString()
            .Trim();
    }
}