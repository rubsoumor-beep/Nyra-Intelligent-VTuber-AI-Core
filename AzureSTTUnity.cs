using UnityEngine;
using UnityEngine.Events;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

[Serializable]
public class TranscriptionEvent : UnityEvent<string> { }

public class AzureSTTUnity : MonoBehaviour
{
    [Header("Azure Speech")]
    [Tooltip("Azure Speech region, por exemplo: brazilsouth")]
    [SerializeField] private string speechRegion = "brazilsouth";

    [Header("Idioma")]
    [SerializeField] private string language = "pt-BR";

    [Header("Nyra Voice")]
    [SerializeField] private AudioSource nyraVoice;

    [Header("Eventos")]
    public TranscriptionEvent onFinalResult =
        new TranscriptionEvent();

    public TranscriptionEvent onInterimResult =
        new TranscriptionEvent();

    private SpeechConfig speechConfig;
    private SpeechRecognizer recognizer;

    private PushAudioInputStream pushStream;
    private AudioConfig audioConfig;

    private bool initialized = false;
    private bool listening = false;
    private bool starting = false;

    // A chave NÃO fica serializada no Unity Inspector.
    // Para uso local, configure a variável de ambiente:
    //
    // AZURE_SPEECH_KEY
    //
    // A chave nunca deve ser colocada diretamente neste arquivo
    // ou commitada no GitHub.

    private string speechKey;

    // ============================================================
    // FILA PARA CALLBACKS DO AZURE
    //
    // O SDK do Azure pode executar seus eventos em uma thread
    // diferente da thread principal do Unity.
    //
    // Os resultados são colocados em filas e entregues pelo
    // Update(), garantindo que UnityEvent e ChatManager sejam
    // executados na thread principal.
    // ============================================================

    private readonly Queue<string> finalResultsQueue =
        new Queue<string>();

    private readonly Queue<string> interimResultsQueue =
        new Queue<string>();

    private readonly object resultLock =
        new object();

    // ============================================================
    // START
    // ============================================================

    private async void Start()
    {
        Debug.Log("[Azure] Inicializando...");

        // ========================================================
        // CARREGAR CREDENCIAIS DO AMBIENTE
        // ========================================================

        speechKey =
            Environment.GetEnvironmentVariable(
                "AZURE_SPEECH_KEY"
            );

        string environmentRegion =
            Environment.GetEnvironmentVariable(
                "AZURE_SPEECH_REGION"
            );

        if (!string.IsNullOrWhiteSpace(environmentRegion))
        {
            speechRegion = environmentRegion.Trim();
        }

        if (string.IsNullOrWhiteSpace(speechKey))
        {
            Debug.LogError(
                "[Azure] AZURE_SPEECH_KEY não configurada."
            );

            Debug.LogError(
                "[Azure] Configure a variável de ambiente " +
                "AZURE_SPEECH_KEY antes de executar a aplicação."
            );

            return;
        }

        if (string.IsNullOrWhiteSpace(speechRegion))
        {
            Debug.LogError(
                "[Azure] Região do Azure Speech não configurada."
            );

            return;
        }

        try
        {
            // ====================================================
            // CONFIGURAÇÃO AZURE
            // ====================================================

            speechConfig =
                SpeechConfig.FromSubscription(
                    speechKey,
                    speechRegion
                );

            speechConfig.SpeechRecognitionLanguage =
                language;

            // ====================================================
            // STREAM PCM
            // ====================================================

            pushStream =
                AudioInputStream.CreatePushStream(
                    AudioStreamFormat.GetWaveFormatPCM(
                        16000,
                        16,
                        1
                    )
                );

            audioConfig =
                AudioConfig.FromStreamInput(
                    pushStream
                );

            // ====================================================
            // RECOGNIZER
            // ====================================================

            recognizer =
                new SpeechRecognizer(
                    speechConfig,
                    audioConfig
                );

            // ====================================================
            // EVENTOS
            // ====================================================

            recognizer.Recognizing +=
                OnRecognizing;

            recognizer.Recognized +=
                OnRecognized;

            recognizer.Canceled +=
                OnCanceled;

            recognizer.SessionStarted +=
                OnSessionStarted;

            recognizer.SessionStopped +=
                OnSessionStopped;

            initialized = true;

            Debug.Log(
                "[Azure] Speech inicializado."
            );

            await StartRecognitionInternal();
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[Azure] Erro inicializando Speech."
            );

            Debug.LogError(
                e.ToString()
            );
        }
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        // ========================================================
        // RESULTADOS FINAIS
        // ========================================================

        while (true)
        {
            string text = null;

            lock (resultLock)
            {
                if (finalResultsQueue.Count > 0)
                {
                    text =
                        finalResultsQueue.Dequeue();
                }
            }

            if (string.IsNullOrWhiteSpace(text))
                break;

            Debug.Log(
                "[Azure] Entregando resultado final ao ChatManager."
            );

            try
            {
                onFinalResult?.Invoke(text);

                Debug.Log(
                    "[Azure] onFinalResult enviado ao ChatManager."
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[Azure] Erro ao entregar resultado ao ChatManager."
                );

                Debug.LogError(
                    e.ToString()
                );
            }
        }

        // ========================================================
        // RESULTADOS INTERMEDIÁRIOS
        // ========================================================

        while (true)
        {
            string text = null;

            lock (resultLock)
            {
                if (interimResultsQueue.Count > 0)
                {
                    text =
                        interimResultsQueue.Dequeue();
                }
            }

            if (string.IsNullOrWhiteSpace(text))
                break;

            try
            {
                onInterimResult?.Invoke(text);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[Azure] Erro entregando resultado intermediário."
                );

                Debug.LogError(
                    e.ToString()
                );
            }
        }
    }

    // ============================================================
    // INICIAR RECONHECIMENTO INTERNO
    // ============================================================

    private async Task StartRecognitionInternal()
    {
        if (!initialized)
        {
            Debug.LogError(
                "[Azure] Ainda não inicializado."
            );

            return;
        }

        if (recognizer == null)
        {
            Debug.LogError(
                "[Azure] Recognizer é NULL."
            );

            return;
        }

        if (listening)
        {
            Debug.Log(
                "[Azure] Reconhecimento já está ativo."
            );

            return;
        }

        if (starting)
        {
            Debug.Log(
                "[Azure] Reconhecimento já está sendo iniciado."
            );

            return;
        }

        starting = true;

        try
        {
            Debug.Log(
                "[Azure] Iniciando reconhecimento contínuo..."
            );

            await recognizer
                .StartContinuousRecognitionAsync();

            listening = true;

            Debug.Log(
                "[Azure] Pronto para transcrever."
            );
        }
        catch (Exception e)
        {
            listening = false;

            Debug.LogError(
                "[Azure] Erro iniciando reconhecimento."
            );

            Debug.LogError(
                e.ToString()
            );
        }
        finally
        {
            starting = false;
        }
    }

    // ============================================================
    // MÉTODO PÚBLICO
    // ============================================================

    public async void StartListening()
    {
        await StartRecognitionInternal();
    }

    // ============================================================
    // RECEBER ÁUDIO DO MICROPHONE CAPTURE
    // ============================================================

    public void SendAudio(byte[] pcmData)
    {
        if (!initialized)
        {
            Debug.LogError(
                "[Azure] Não inicializado."
            );

            return;
        }

        if (pushStream == null)
        {
            Debug.LogError(
                "[Azure] PushAudioInputStream é NULL."
            );

            return;
        }

        if (!listening)
        {
            Debug.LogWarning(
                "[Azure] Reconhecimento não está ativo. " +
                "Áudio descartado."
            );

            return;
        }

        if (pcmData == null ||
            pcmData.Length == 0)
        {
            Debug.LogWarning(
                "[Azure] Áudio vazio."
            );

            return;
        }

        try
        {
            Debug.Log(
                "[Azure] Recebidos " +
                pcmData.Length +
                " bytes."
            );

            pushStream.Write(
                pcmData
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[Azure] Erro enviando áudio."
            );

            Debug.LogError(
                e.ToString()
            );
        }
    }

    // ============================================================
    // INTERIM
    // ============================================================

    private void OnRecognizing(
        object sender,
        SpeechRecognitionEventArgs e
    )
    {
        if (e == null ||
            e.Result == null)
            return;

        string text =
            e.Result.Text;

        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (resultLock)
        {
            interimResultsQueue.Enqueue(text);
        }
    }

    // ============================================================
    // FINAL
    // ============================================================

    private void OnRecognized(
        object sender,
        SpeechRecognitionEventArgs e
    )
    {
        if (e == null ||
            e.Result == null)
            return;

        Debug.Log(
            "[Azure] Resultado recebido. Reason: " +
            e.Result.Reason
        );

        if (
            e.Result.Reason ==
            ResultReason.RecognizedSpeech
        )
        {
            string text =
                e.Result.Text;

            // ====================================================
            // FALLBACK JSON
            // ====================================================

            if (string.IsNullOrWhiteSpace(text))
            {
                string rawJson = "";

                try
                {
                    rawJson =
                        e.Result.Properties.GetProperty(
                            PropertyId.SpeechServiceResponse_JsonResult
                        );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        "[Azure] Não foi possível obter JSON bruto: " +
                        ex.Message
                    );
                }

                text =
                    ExtractDisplayTextFromJson(
                        rawJson
                    );

                if (string.IsNullOrWhiteSpace(text))
                {
                    Debug.LogWarning(
                        "[Azure] Não foi possível recuperar texto " +
                        "do resultado."
                    );

                    return;
                }
            }

            text =
                text.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return;

            Debug.Log(
                "[Azure] Resultado final reconhecido."
            );

            // ====================================================
            // THREAD SAFETY
            // ====================================================

            lock (resultLock)
            {
                finalResultsQueue.Enqueue(text);
            }
        }
        else if (
            e.Result.Reason ==
            ResultReason.NoMatch
        )
        {
            Debug.LogWarning(
                "[Azure] Áudio recebido, mas a fala não foi reconhecida."
            );
        }
    }

    // ============================================================
    // FALLBACK: EXTRAIR TEXTO DO JSON
    // ============================================================

    private string ExtractDisplayTextFromJson(
        string json
    )
    {
        if (string.IsNullOrEmpty(json))
            return null;

        Match match =
            Regex.Match(
                json,
                "\"DisplayText\"\\s*:\\s*\"(.*?)(?<!\\\\)\""
            );

        if (match.Success)
        {
            string raw =
                match.Groups[1].Value;

            raw =
                raw
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", " ")
                    .Replace("\\t", " ");

            return raw;
        }

        return null;
    }

    // ============================================================
    // SESSION STARTED
    // ============================================================

    private void OnSessionStarted(
        object sender,
        SessionEventArgs e
    )
    {
        Debug.Log(
            "[Azure] Sessão de reconhecimento iniciada."
        );
    }

    // ============================================================
    // CANCELADO
    // ============================================================

    private void OnCanceled(
        object sender,
        SpeechRecognitionCanceledEventArgs e
    )
    {
        listening = false;

        Debug.LogError(
            "[Azure] Reconhecimento cancelado."
        );

        Debug.LogError(
            "Reason: " +
            e.Reason
        );

        Debug.LogError(
            "ErrorCode: " +
            e.ErrorCode
        );

        // ErrorDetails pode conter informações úteis para
        // diagnóstico, mas não deve ser usado para armazenar
        // credenciais ou segredos.
        if (!string.IsNullOrWhiteSpace(e.ErrorDetails))
        {
            Debug.LogError(
                "Details: " +
                e.ErrorDetails
            );
        }
    }

    // ============================================================
    // SESSION STOPPED
    // ============================================================

    private void OnSessionStopped(
        object sender,
        SessionEventArgs e
    )
    {
        listening = false;

        Debug.Log(
            "[Azure] Sessão encerrada."
        );
    }

    // ============================================================
    // PARAR
    // ============================================================

    public async void StopListening()
    {
        if (recognizer == null)
            return;

        if (!listening)
            return;

        try
        {
            Debug.Log(
                "[Azure] Parando reconhecimento..."
            );

            await recognizer
                .StopContinuousRecognitionAsync();

            listening = false;

            Debug.Log(
                "[Azure] Reconhecimento parado."
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[Azure] Erro parando reconhecimento."
            );

            Debug.LogError(
                e.ToString()
            );
        }
    }

    // ============================================================
    // STATUS
    // ============================================================

    public bool IsListening()
    {
        return listening;
    }

    // ============================================================
    // DESTROY
    // ============================================================

    private async void OnDestroy()
    {
        try
        {
            if (recognizer != null)
            {
                if (listening)
                {
                    await recognizer
                        .StopContinuousRecognitionAsync();
                }

                recognizer.Recognizing -=
                    OnRecognizing;

                recognizer.Recognized -=
                    OnRecognized;

                recognizer.Canceled -=
                    OnCanceled;

                recognizer.SessionStarted -=
                    OnSessionStarted;

                recognizer.SessionStopped -=
                    OnSessionStopped;

                recognizer.Dispose();

                recognizer = null;
            }

            if (pushStream != null)
            {
                pushStream.Close();
                pushStream.Dispose();
                pushStream = null;
            }

            if (audioConfig != null)
            {
                audioConfig.Dispose();
                audioConfig = null;
            }

            speechConfig = null;
            speechKey = null;

            initialized = false;
            listening = false;
            starting = false;

            lock (resultLock)
            {
                finalResultsQueue.Clear();
                interimResultsQueue.Clear();
            }
        }
        catch
        {
            // Cleanup deve permanecer silencioso durante destruição
            // do componente.
        }
    }
}