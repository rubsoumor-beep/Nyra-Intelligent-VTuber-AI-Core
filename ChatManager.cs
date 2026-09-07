using UnityEngine;
using System.Collections;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Serialization;


// ============================================================
// CHAT REQUEST
// ============================================================

[System.Serializable]
public class ChatRequest
{
    public string prompt;
    public string userId;

    public ChatRequest(string prompt, string userId)
    {
        this.prompt = prompt;
        this.userId = userId;
    }
}


// ============================================================
// NODE RESPONSE
// ============================================================

[System.Serializable]
public class NodeResponse
{
    public string reply;

    public string emotion;
    public string emocao;

    public string intent;
    public string intencao;

    public float intensity;
    public float intensidade;

    public float score;
}


// ============================================================
// STREAM MESSAGE
// ============================================================

[System.Serializable]
public class StreamMessage
{
    public string type;

    public string text;
    public string reply;

    public string emocao;
    public string emotion;

    public string intencao;
    public string intent;

    public float intensidade;
    public float intensity;

    public string error;
}


// ============================================================
// VISION COMMENT RESPONSE
// ============================================================

[System.Serializable]
public class VisionCommentResponse
{
    public string comentario;
    public string comment;

    public string reply;
    public string text;

    public string emocao;
    public string emotion;

    public string intencao;
    public string intent;

    public float intensidade;
    public float intensity;

    public float score;

    public bool falar;
    public bool speak;
    public bool pending;
}


// ============================================================
// DOWNLOAD HANDLER PARA NDJSON STREAMING
// ============================================================

public class NDJSONDownloadHandler : DownloadHandlerScript
{
    private readonly Queue<string> lines =
        new Queue<string>();

    private readonly object lineLock =
        new object();

    private readonly List<byte> pendingBytes =
        new List<byte>();


    public NDJSONDownloadHandler()
        : base(new byte[8192])
    {
    }


    protected override bool ReceiveData(
        byte[] data,
        int dataLength
    )
    {
        if (data == null || dataLength <= 0)
            return true;

        lock (lineLock)
        {
            for (int i = 0; i < dataLength; i++)
            {
                byte b = data[i];

                if (b == (byte)'\n')
                {
                    if (pendingBytes.Count > 0)
                    {
                        string line =
                            Encoding.UTF8.GetString(
                                pendingBytes.ToArray()
                            ).Trim();

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lines.Enqueue(line);
                        }

                        pendingBytes.Clear();
                    }
                }
                else if (b != (byte)'\r')
                {
                    pendingBytes.Add(b);
                }
            }
        }

        return true;
    }


    protected override void CompleteContent()
    {
        lock (lineLock)
        {
            if (pendingBytes.Count > 0)
            {
                string line =
                    Encoding.UTF8.GetString(
                        pendingBytes.ToArray()
                    ).Trim();

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Enqueue(line);
                }

                pendingBytes.Clear();
            }
        }

        Debug.Log(
            "[NYRA STREAM] DownloadHandler recebeu fim do conteúdo."
        );
    }


    public bool TryDequeue(
        out string line
    )
    {
        lock (lineLock)
        {
            if (lines.Count > 0)
            {
                line = lines.Dequeue();
                return true;
            }
        }

        line = null;
        return false;
    }


    public int PendingCount
    {
        get
        {
            lock (lineLock)
            {
                return lines.Count;
            }
        }
    }
}


// ============================================================
// CHAT MANAGER
// ============================================================

public class ChatManager : MonoBehaviour
{
    // ========================================================
    // LOGIN
    // ========================================================

    [Header("Login")]

    public TMP_InputField userIdInputField;

    public GameObject loginPanel;

    public GameObject chatPanel;

    public TMP_Text userInfoText;

    public string[] allowedUserIds =
    {
        "Rubens",
        "Admin",
        "Teste"
    };

    private string currentUserId;


    // ========================================================
    // CHAT
    // ========================================================

    [Header("Chat")]

    public TMP_InputField userInputField;

    public TMP_Text chatDisplay;

    public ScrollRect chatScrollRect;


    // ========================================================
    // AZURE STT
    // ========================================================

    [Header("Voice - Azure STT")]

    [FormerlySerializedAs("googleSTT")]
    public AzureSTTUnity azureSTT;

    public AudioSource audioSource;


    // ========================================================
    // AZURE TTS
    // ========================================================

    [Header("Backend - Azure TTS")]

    [FormerlySerializedAs("googleTTS")]
    public AzureTTS_Rest azureTTS;


    // ========================================================
    // AVATAR
    // ========================================================

    [Header("Avatar Control")]

    public GameObject avatar;

    public Vector3 hiddenPosition =
        new Vector3(0, -1000, 0);

    private Vector3 avatarOriginalPosition;


    // ========================================================
    // SERVIDOR NODE
    // ========================================================

    [Header("Node Server")]

    [Tooltip(
        "Caminho para o server.js. " +
        "Configure no Inspector. " +
        "Nenhum caminho local é armazenado no código."
    )]
    public string nodeServerPath = "";

    private System.Diagnostics.Process nodeProcess;


    // ========================================================
    // ENDPOINTS
    // ========================================================

    [Header("Backend Endpoints")]

    [Tooltip(
        "Endpoint usado para comunicação com o backend Node."
    )]
    public string chatStreamUrl =
        "http://localhost:3000/chat-stream";

    [Tooltip(
        "Endpoint usado para comentários espontâneos da visão."
    )]
    public string visionCommentUrl =
        "http://localhost:3000/vision/comentario";


    // ========================================================
    // ESTADO
    // ========================================================

    private bool isProcessing = false;

    private bool isNyraSpeaking = false;

    private float voiceCooldownUntil = 0f;

    private string lastRecognizedText = "";

    private float lastRecognizedTime = -10f;


    // ========================================================
    // STREAMING
    // ========================================================

    private NDJSONDownloadHandler activeStreamHandler;

    private StringBuilder streamSpeechBuffer =
        new StringBuilder();

    private StringBuilder streamFullResponse =
        new StringBuilder();

    private bool streamHasText = false;

    private bool streamReceivedText = false;

    private bool streamTTSStarted = false;

    private bool streamFinished = false;

    private bool streamDisplayCreated = false;


    // ========================================================
    // EXPRESSIVIDADE
    // ========================================================

    private string streamEmotion = "neutral";

    private string streamIntent = "assertion";

    private float streamIntensity = 0.35f;


    // ========================================================
    // CONFIGURAÇÃO DE FRASES
    // ========================================================

    private const int MIN_COMMA_CUT_LENGTH = 45;

    private const int MIN_FORCED_CUT_LENGTH = 65;

    private const int MAX_SPEECH_SEGMENT_LENGTH = 120;

    private const int MIN_FIRST_SENTENCE_LENGTH = 18;


    // ========================================================
    // VISÃO ESPONTÂNEA
    // ========================================================

    [Header("Nyra - Visão Espontânea")]

    public bool enableSpontaneousVision = true;

    [Tooltip("Tempo entre consultas ao comentário visual.")]
    [Range(5f, 300f)]
    public float visionPollInterval = 45f;

    [Tooltip("Tempo mínimo entre comentários espontâneos.")]
    [Range(5f, 600f)]
    public float visionSpeechCooldown = 35f;

    [Tooltip(
        "Se falso, a Nyra não interrompe uma conversa para comentar."
    )]
    public bool visionCanInterruptChat = false;

    [Tooltip(
        "Mostra informações detalhadas da visão no console."
    )]
    public bool visionDebugLogs = true;

    private bool visionRequestRunning = false;

    private bool visionSpeaking = false;

    private float nextVisionPollTime = 0f;

    private float nextVisionSpeechTime = 0f;


    // ========================================================
    // START
    // ========================================================

    IEnumerator Start()
    {
        if (loginPanel != null)
            loginPanel.SetActive(true);

        if (chatPanel != null)
            chatPanel.SetActive(false);


        // ========================================================
        // AVATAR
        // ========================================================

        if (avatar != null)
        {
            avatarOriginalPosition =
                avatar.transform.position;
        }

        MoveAvatar(false);


        // ========================================================
        // NODE SERVER
        // ========================================================

        StartNodeServer();


        yield return new WaitForSeconds(3f);


        // ========================================================
        // AZURE STT
        // ========================================================

        if (azureSTT == null)
        {
            Debug.LogError(
                "[ChatManager] AzureSTTUnity não atribuído."
            );
        }
        else
        {
            Debug.Log(
                "[ChatManager] AzureSTTUnity atribuído: " +
                azureSTT.name
            );

            azureSTT.onFinalResult.RemoveListener(
                OnVoiceRecognized
            );

            azureSTT.onFinalResult.AddListener(
                OnVoiceRecognized
            );

            Debug.Log(
                "[ChatManager] Azure STT conectado ao ChatManager."
            );
        }


        // ========================================================
        // MICROFONE
        // ========================================================

        string[] devices =
            Microphone.devices;


        if (
            devices == null ||
            devices.Length == 0
        )
        {
            AddMessage(
                "Sistema",
                "Nenhum microfone disponível. Funções de voz desativadas."
            );
        }
        else
        {
            AddMessage(
                "Sistema",
                "Microfone detectado."
            );
        }


        // ========================================================
        // VISÃO
        // ========================================================

        if (enableSpontaneousVision)
        {
            nextVisionPollTime =
                Time.time + 8f;

            Debug.Log(
                "[NYRA] Visão espontânea ativada."
            );
        }


        FocusLoginField();
    }


    // ========================================================
    // UPDATE
    // ========================================================

    void Update()
    {
        ProcessarEventosStreaming();


        // ========================================================
        // VISÃO ESPONTÂNEA
        // ========================================================

        if (
            enableSpontaneousVision &&
            Time.time >= nextVisionPollTime &&
            !visionRequestRunning
        )
        {
            nextVisionPollTime =
                Time.time + visionPollInterval;

            StartCoroutine(
                ConsultarComentarioVisual()
            );
        }


        // ========================================================
        // ENTER
        // ========================================================

        if (
            Input.GetKeyDown(KeyCode.Return) ||
            Input.GetKeyDown(KeyCode.KeypadEnter)
        )
        {
            if (
                loginPanel != null &&
                loginPanel.activeSelf
            )
            {
                OnLoginButtonClicked();

                return;
            }


            if (
                chatPanel != null &&
                chatPanel.activeSelf
            )
            {
                if (
                    userInputField != null &&
                    !string.IsNullOrWhiteSpace(
                        userInputField.text
                    ) &&
                    !isProcessing
                )
                {
                    OnSendButtonClicked();
                }
            }
        }


        MaintainInputFocus();
    }


    // ========================================================
    // APPLICATION QUIT
    // ========================================================

    void OnApplicationQuit()
    {
        if (azureSTT != null)
        {
            try
            {
                azureSTT.StopListening();
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "Erro ao parar Azure STT: " +
                    e.Message
                );
            }
        }


        if (azureTTS != null)
        {
            try
            {
                azureTTS.StopStreaming();
            }
            catch
            {
            }
        }


        if (
            nodeProcess != null &&
            !nodeProcess.HasExited
        )
        {
            try
            {
                nodeProcess.Kill();
            }
            catch
            {
            }
        }
    }


    // ========================================================
    // NODE SERVER
    // ========================================================

    void StartNodeServer()
    {
        if (string.IsNullOrWhiteSpace(nodeServerPath))
        {
            Debug.LogWarning(
                "[ChatManager] nodeServerPath não configurado. " +
                "O servidor Node não será iniciado automaticamente."
            );

            return;
        }


        try
        {
            string fullPath =
                System.IO.Path.GetFullPath(
                    nodeServerPath
                );


            if (!System.IO.File.Exists(fullPath))
            {
                Debug.LogError(
                    "[ChatManager] server.js não encontrado no caminho configurado."
                );

                return;
            }


            string directory =
                System.IO.Path.GetDirectoryName(
                    fullPath
                );

            string fileName =
                System.IO.Path.GetFileName(
                    fullPath
                );


            nodeProcess =
                new System.Diagnostics.Process();


            nodeProcess.StartInfo.FileName =
                "cmd.exe";


            nodeProcess.StartInfo.Arguments =
                $"/c cd /d \"{directory}\" && node \"{fileName}\"";


            nodeProcess.StartInfo.CreateNoWindow =
                true;


            nodeProcess.StartInfo.UseShellExecute =
                false;


            nodeProcess.Start();


            Debug.Log(
                "[ChatManager] Node server iniciado."
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[ChatManager] Erro ao iniciar Node Server: " +
                e.Message
            );
        }
    }


    // ========================================================
    // LOGIN
    // ========================================================

    public void OnLoginButtonClicked()
    {
        if (userIdInputField == null)
            return;


        string inputId =
            userIdInputField.text.Trim();


        if (string.IsNullOrEmpty(inputId))
        {
            AddMessage(
                "Sistema",
                "ID de usuário vazio."
            );

            MoveAvatar(false);

            FocusLoginField();

            return;
        }


        if (
            allowedUserIds != null &&
            (
                allowedUserIds.Length == 0 ||
                Array.Exists(
                    allowedUserIds,
                    id => id == inputId
                )
            )
        )
        {
            currentUserId =
                inputId;


            if (userInfoText != null)
            {
                userInfoText.text =
                    $"Logado como: {currentUserId}";
            }


            if (loginPanel != null)
                loginPanel.SetActive(false);

            if (chatPanel != null)
                chatPanel.SetActive(true);


            MoveAvatar(true);


            AddMessage(
                "Sistema",
                $"Bem-vindo, {currentUserId}."
            );


            FocusChatField();
        }
        else
        {
            AddMessage(
                "Sistema",
                "ID de usuário inválido."
            );


            MoveAvatar(false);

            FocusLoginField();
        }
    }


    // ========================================================
    // AVATAR
    // ========================================================

    private void MoveAvatar(bool show)
    {
        if (avatar == null)
            return;


        avatar.transform.position =
            show
                ? avatarOriginalPosition
                : hiddenPosition;
    }


    // ========================================================
    // BOTÃO ENVIAR
    // ========================================================

    public void OnSendButtonClicked()
    {
        if (userInputField == null)
            return;


        string textToSend =
            userInputField.text.Trim();


        if (
            string.IsNullOrEmpty(textToSend) ||
            string.IsNullOrEmpty(currentUserId) ||
            isProcessing
        )
        {
            return;
        }


        userInputField.text = "";

        userInputField.ReleaseSelection();


        SendText(textToSend);
    }


    // ========================================================
    // SEND TEXT
    // ========================================================

    public void SendText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;


        if (string.IsNullOrEmpty(currentUserId))
            return;


        if (isProcessing)
        {
            Debug.Log(
                "SendText ignorado: já existe uma requisição."
            );

            return;
        }


        AddMessage(
            $"Você ({currentUserId})",
            text
        );


        StartCoroutine(
            SendTextToBackend(text)
        );
    }


    // ========================================================
    // BACKEND STREAM
    // ========================================================

    IEnumerator SendTextToBackend(
        string text
    )
    {
        isProcessing = true;


        ResetStreamingState();


        var requestData =
            new ChatRequest(
                text,
                currentUserId
            );


        string json =
            JsonUtility.ToJson(
                requestData
            );


        int maxRetries = 5;

        int attempt = 0;

        bool success = false;


        while (
            !success &&
            attempt < maxRetries
        )
        {
            attempt++;


            Debug.Log(
                $"[NYRA STREAM] Enviando pergunta. Tentativa {attempt}"
            );


            activeStreamHandler =
                new NDJSONDownloadHandler();


            using (
                UnityWebRequest request =
                    new UnityWebRequest(
                        chatStreamUrl,
                        "POST"
                    )
            )
            {
                request.uploadHandler =
                    new UploadHandlerRaw(
                        Encoding.UTF8.GetBytes(
                            json
                        )
                    );


                request.downloadHandler =
                    activeStreamHandler;


                request.disposeDownloadHandlerOnDispose =
                    false;


                request.SetRequestHeader(
                    "Content-Type",
                    "application/json"
                );


                request.SetRequestHeader(
                    "Accept",
                    "application/x-ndjson"
                );


                request.timeout = 0;


                UnityWebRequestAsyncOperation operation =
                    request.SendWebRequest();


                while (!operation.isDone)
                {
                    ProcessarEventosStreaming();

                    yield return null;
                }


                ProcessarEventosStreaming();


                if (
                    request.result ==
                        UnityWebRequest.Result.Success &&
                    streamHasText
                )
                {
                    success = true;


                    Debug.Log(
                        "[NYRA STREAM] Resposta recebida progressivamente."
                    );
                }
                else
                {
                    Debug.LogWarning(
                        $"[NYRA STREAM] Tentativa {attempt} falhou. " +
                        $"HTTP: {request.responseCode}"
                    );


                    if (streamHasText)
                    {
                        success = true;
                    }
                    else
                    {
                        yield return new WaitForSeconds(
                            1.5f
                        );
                    }
                }
            }


            activeStreamHandler = null;
        }


        // ========================================================
        // FINALIZAR
        // ========================================================

        if (success)
        {
            if (!streamFinished)
            {
                FinalizarStreaming(
                    streamFullResponse.ToString(),
                    streamEmotion,
                    streamIntent,
                    streamIntensity
                );
            }


            if (
                azureTTS != null &&
                !streamTTSStarted &&
                streamHasText
            )
            {
                yield return StartCoroutine(
                    IniciarTTSDaRespostaCompleta()
                );
            }


            if (azureTTS != null)
            {
                yield return StartCoroutine(
                    azureTTS.WaitForStreamingComplete()
                );
            }


            isNyraSpeaking = false;


            Debug.Log(
                "Nyra terminou de falar."
            );


            voiceCooldownUntil =
                Time.time + 0.8f;


            yield return new WaitForSeconds(
                0.8f
            );
        }
        else
        {
            AddMessage(
                "Sistema",
                "O servidor parece estar offline. Verifique o console."
            );
        }


        // ========================================================
        // LIBERAR PROCESSAMENTO
        // ========================================================

        isProcessing = false;


        // ========================================================
        // REATIVAR STT
        // ========================================================

        if (
            success &&
            azureSTT != null &&
            !isNyraSpeaking
        )
        {
            if (
                Time.time <
                voiceCooldownUntil
            )
            {
                float remaining =
                    voiceCooldownUntil -
                    Time.time;


                if (remaining > 0f)
                {
                    yield return new WaitForSeconds(
                        remaining
                    );
                }
            }


            try
            {
                azureSTT.StartListening();


                Debug.Log(
                    "Azure STT reativado."
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "Erro ao reativar Azure STT: " +
                    e.Message
                );
            }
        }


        FocusChatField();
    }


    // ========================================================
    // RESET STREAMING
    // ========================================================

    private void ResetStreamingState()
    {
        if (azureTTS != null)
        {
            try
            {
                azureTTS.StopStreaming();
            }
            catch
            {
            }
        }


        streamSpeechBuffer.Clear();

        streamFullResponse.Clear();


        streamHasText = false;

        streamReceivedText = false;

        streamTTSStarted = false;

        streamFinished = false;

        streamDisplayCreated = false;


        streamEmotion = "neutral";

        streamIntent = "assertion";

        streamIntensity = 0.35f;
    }


    // ========================================================
    // PROCESSAR EVENTOS DO STREAM
    // ========================================================

    private void ProcessarEventosStreaming()
    {
        NDJSONDownloadHandler handler =
            activeStreamHandler;


        if (handler == null)
            return;


        while (
            handler.TryDequeue(
                out string line
            )
        )
        {
            try
            {
                StreamMessage msg =
                    JsonUtility.FromJson<StreamMessage>(
                        line
                    );


                if (msg == null)
                    continue;


                ProcessarEventoStream(
                    msg
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "Linha NDJSON inválida: " +
                    e.Message
                );
            }
        }
    }


    // ========================================================
    // PROCESSAR EVENTO
    // ========================================================

    private void ProcessarEventoStream(
        StreamMessage msg
    )
    {
        if (msg == null)
            return;


        switch (msg.type)
        {
            case "start":

                if (
                    !string.IsNullOrWhiteSpace(
                        msg.emocao
                    )
                )
                {
                    streamEmotion =
                        msg.emocao;
                }
                else if (
                    !string.IsNullOrWhiteSpace(
                        msg.emotion
                    )
                )
                {
                    streamEmotion =
                        msg.emotion;
                }


                if (
                    !string.IsNullOrWhiteSpace(
                        msg.intencao
                    )
                )
                {
                    streamIntent =
                        msg.intencao;
                }
                else if (
                    !string.IsNullOrWhiteSpace(
                        msg.intent
                    )
                )
                {
                    streamIntent =
                        msg.intent;
                }


                AtualizarIntensidade(msg);


                Debug.Log(
                    "[NYRA STREAM] Iniciado. " +
                    "Emoção: " + streamEmotion +
                    " | Intenção: " + streamIntent +
                    " | Intensidade: " +
                    streamIntensity.ToString("0.00")
                );


                break;


            case "text":

                if (
                    string.IsNullOrEmpty(
                        msg.text
                    )
                )
                {
                    return;
                }


                ReceberTextoStreaming(
                    msg.text
                );


                break;


            case "emotion":

                if (
                    !string.IsNullOrWhiteSpace(
                        msg.emocao
                    )
                )
                {
                    streamEmotion =
                        msg.emocao;
                }
                else if (
                    !string.IsNullOrWhiteSpace(
                        msg.emotion
                    )
                )
                {
                    streamEmotion =
                        msg.emotion;
                }


                if (
                    !string.IsNullOrWhiteSpace(
                        msg.intencao
                    )
                )
                {
                    streamIntent =
                        msg.intencao;
                }
                else if (
                    !string.IsNullOrWhiteSpace(
                        msg.intent
                    )
                )
                {
                    streamIntent =
                        msg.intent;
                }


                AtualizarIntensidade(msg);


                Debug.Log(
                    "[NYRA STREAM] Expressividade atualizada. " +
                    "Emoção=" + streamEmotion +
                    " | Intenção=" + streamIntent +
                    " | Intensidade=" +
                    streamIntensity.ToString("0.00")
                );


                break;


            case "done":

                string emocaoFinal =
                    !string.IsNullOrWhiteSpace(
                        msg.emocao
                    )
                        ? msg.emocao
                        : msg.emotion;


                string intencaoFinal =
                    !string.IsNullOrWhiteSpace(
                        msg.intencao
                    )
                        ? msg.intencao
                        : msg.intent;


                if (
                    !string.IsNullOrWhiteSpace(
                        emocaoFinal
                    )
                )
                {
                    streamEmotion =
                        emocaoFinal;
                }


                if (
                    !string.IsNullOrWhiteSpace(
                        intencaoFinal
                    )
                )
                {
                    streamIntent =
                        intencaoFinal;
                }


                AtualizarIntensidade(msg);


                FinalizarStreaming(
                    msg.reply,
                    streamEmotion,
                    streamIntent,
                    streamIntensity
                );


                break;


            case "error":

                Debug.LogError(
                    "[NYRA STREAM] Erro recebido do backend."
                );


                streamFinished = true;


                break;
        }
    }


    // ========================================================
    // ATUALIZAR INTENSIDADE
    // ========================================================

    private void AtualizarIntensidade(
        StreamMessage msg
    )
    {
        if (msg == null)
            return;


        float valor;


        if (
            msg.intensidade > 0f
        )
        {
            valor =
                msg.intensidade;
        }
        else if (
            msg.intensity > 0f
        )
        {
            valor =
                msg.intensity;
        }
        else
        {
            return;
        }


        streamIntensity =
            Mathf.Clamp01(
                valor
            );
    }


    // ========================================================
    // RECEBER TEXTO DO BACKEND
    // ========================================================

    private void ReceberTextoStreaming(
        string chunk
    )
    {
        streamHasText = true;


        streamFullResponse.Append(
            chunk
        );


        streamSpeechBuffer.Append(
            chunk
        );


        // ========================================================
        // PRIMEIRO TEXTO
        // ========================================================

        if (!streamReceivedText)
        {
            streamReceivedText = true;


            if (azureSTT != null)
            {
                try
                {
                    azureSTT.StopListening();


                    Debug.Log(
                        "Azure STT parado: Nyra recebeu o primeiro trecho."
                    );
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "Erro parando STT: " +
                        e.Message
                    );
                }
            }


            isNyraSpeaking = true;


            Debug.Log(
                "[NYRA] Texto recebido. " +
                "TTS aguardando expressividade final."
            );
        }


        // ========================================================
        // TEXTO NA INTERFACE
        // ========================================================

        if (chatDisplay != null)
        {
            if (!streamDisplayCreated)
            {
                chatDisplay.text +=
                    "\n<b>Nyra:</b> " +
                    EscaparRichText(chunk);


                streamDisplayCreated = true;
            }
            else
            {
                chatDisplay.text +=
                    EscaparRichText(chunk);
            }


            Canvas.ForceUpdateCanvases();


            if (chatScrollRect != null)
            {
                chatScrollRect.verticalNormalizedPosition =
                    0f;
            }
        }
    }


    // ========================================================
    // INICIAR TTS DA RESPOSTA COMPLETA
    // ========================================================

    private IEnumerator IniciarTTSDaRespostaCompleta()
    {
        if (azureTTS == null)
        {
            Debug.LogWarning(
                "AzureTTS não configurado."
            );

            yield break;
        }


        string textoCompleto =
            streamFullResponse
                .ToString()
                .Trim();


        if (string.IsNullOrWhiteSpace(textoCompleto))
        {
            Debug.LogWarning(
                "Texto completo vazio. TTS cancelado."
            );

            yield break;
        }


        Debug.Log(
            "[NYRA] Expressividade final aplicada ao TTS. " +
            "Emoção=" + streamEmotion +
            " | Intenção=" + streamIntent +
            " | Intensidade=" +
            streamIntensity.ToString("0.00")
        );


        azureTTS.BeginStreaming(
            streamEmotion,
            streamIntent,
            streamIntensity
        );


        streamTTSStarted = true;


        streamSpeechBuffer =
            new StringBuilder(
                textoCompleto
            );


        ProcessarFrasesProntas();


        string restante =
            streamSpeechBuffer
                .ToString()
                .Trim();


        if (
            !string.IsNullOrWhiteSpace(
                restante
            )
        )
        {
            EnfileirarFraseStreaming(
                restante
            );

            streamSpeechBuffer.Clear();
        }


        azureTTS.EndStreaming();


        yield return null;
    }


    // ========================================================
    // PROCESSAR FRASES NATURAIS
    // ========================================================

    private void ProcessarFrasesProntas()
    {
        while (true)
        {
            string buffer =
                streamSpeechBuffer.ToString();


            if (string.IsNullOrWhiteSpace(buffer))
                break;


            // ====================================================
            // 1. FRASE COMPLETA
            // ====================================================

            int sentenceIndex =
                EncontrarFimDeFrase(
                    buffer
                );


            if (sentenceIndex >= 0)
            {
                string frase =
                    buffer.Substring(
                        0,
                        sentenceIndex + 1
                    ).Trim();


                string restante =
                    buffer.Substring(
                        sentenceIndex + 1
                    );


                if (
                    frase.Length <
                    MIN_FIRST_SENTENCE_LENGTH &&
                    !string.IsNullOrWhiteSpace(
                        restante
                    )
                )
                {
                    int proximoFim =
                        EncontrarFimDeFrase(
                            restante
                        );


                    if (proximoFim >= 0)
                    {
                        frase =
                            buffer.Substring(
                                0,
                                sentenceIndex + 1 +
                                proximoFim + 1
                            ).Trim();


                        restante =
                            restante.Substring(
                                proximoFim + 1
                            );
                    }
                    else
                    {
                        break;
                    }
                }


                streamSpeechBuffer =
                    new StringBuilder(
                        restante
                    );


                if (
                    !string.IsNullOrWhiteSpace(
                        frase
                    )
                )
                {
                    EnfileirarFraseStreaming(
                        frase
                    );
                }


                continue;
            }


            // ====================================================
            // 2. VÍRGULA NATURAL
            // ====================================================

            if (
                buffer.Length >=
                MIN_COMMA_CUT_LENGTH
            )
            {
                int commaIndex =
                    EncontrarCortePorVirgula(
                        buffer
                    );


                if (commaIndex >= 0)
                {
                    string frase =
                        buffer.Substring(
                            0,
                            commaIndex + 1
                        ).Trim();


                    string restante =
                        buffer.Substring(
                            commaIndex + 1
                        );


                    if (
                        frase.Length >=
                        MIN_COMMA_CUT_LENGTH
                    )
                    {
                        streamSpeechBuffer =
                            new StringBuilder(
                                restante
                            );


                        EnfileirarFraseStreaming(
                            frase
                        );


                        continue;
                    }
                }
            }


            // ====================================================
            // 3. BUFFER MUITO GRANDE
            // ====================================================

            if (
                buffer.Length >=
                MAX_SPEECH_SEGMENT_LENGTH
            )
            {
                int corte =
                    EncontrarMelhorCortePorEspaco(
                        buffer
                    );


                if (corte > 0)
                {
                    string frase =
                        buffer.Substring(
                            0,
                            corte
                        ).Trim();


                    string restante =
                        buffer.Substring(
                            corte
                        );


                    if (
                        frase.Length >=
                        MIN_FORCED_CUT_LENGTH
                    )
                    {
                        streamSpeechBuffer =
                            new StringBuilder(
                                restante
                            );


                        EnfileirarFraseStreaming(
                            frase
                        );


                        continue;
                    }
                }
            }


            break;
        }
    }


    // ========================================================
    // ENCONTRAR FIM DE FRASE
    // ========================================================

    private int EncontrarFimDeFrase(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
            return -1;


        for (
            int i = 0;
            i < texto.Length;
            i++
        )
        {
            char c =
                texto[i];


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


                if (
                    i == texto.Length - 1 ||
                    char.IsWhiteSpace(
                        texto[i + 1]
                    )
                )
                {
                    return i;
                }
            }
        }


        return -1;
    }


    // ========================================================
    // CORTE POR VÍRGULA
    // ========================================================

    private int EncontrarCortePorVirgula(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
            return -1;


        int melhorIndice = -1;


        for (
            int i = 0;
            i < texto.Length;
            i++
        )
        {
            if (texto[i] != ',')
                continue;


            if (
                i + 1 >= texto.Length ||
                !char.IsWhiteSpace(
                    texto[i + 1]
                )
            )
            {
                continue;
            }


            if (
                i < MIN_COMMA_CUT_LENGTH
            )
            {
                continue;
            }


            melhorIndice = i;


            if (i <= 85)
            {
                break;
            }
        }


        return melhorIndice;
    }


    // ========================================================
    // MELHOR CORTE POR ESPAÇO
    // ========================================================

    private int EncontrarMelhorCortePorEspaco(
        string texto
    )
    {
        if (string.IsNullOrEmpty(texto))
            return -1;


        int limite =
            Mathf.Min(
                MAX_SPEECH_SEGMENT_LENGTH,
                texto.Length
            );


        int corte =
            texto.LastIndexOf(
                ' ',
                limite - 1
            );


        if (
            corte <
            MIN_FORCED_CUT_LENGTH
        )
        {
            return -1;
        }


        return corte;
    }


    // ========================================================
    // ENFILEIRAR FRASE NO TTS
    // ========================================================

    private void EnfileirarFraseStreaming(
        string frase
    )
    {
        if (azureTTS == null)
        {
            Debug.LogWarning(
                "AzureTTS não configurado."
            );

            return;
        }


        string limpa =
            frase.Trim();


        if (
            string.IsNullOrWhiteSpace(
                limpa
            )
        )
        {
            return;
        }


        azureTTS.EnqueuePhrase(
            limpa,
            streamEmotion,
            streamIntent,
            streamIntensity
        );


        Debug.Log(
            "[NYRA STREAM] Frase enviada ao TTS. " +
            "Emoção=" +
            streamEmotion +
            " | Intenção=" +
            streamIntent +
            " | Intensidade=" +
            streamIntensity.ToString("0.00")
        );
    }


    // ========================================================
    // FINALIZAR STREAMING
    // ========================================================

    private void FinalizarStreaming(
        string reply,
        string emocao,
        string intencao,
        float intensidade
    )
    {
        if (
            !string.IsNullOrWhiteSpace(
                emocao
            )
        )
        {
            streamEmotion =
                emocao;
        }


        if (
            !string.IsNullOrWhiteSpace(
                intencao
            )
        )
        {
            streamIntent =
                intencao;
        }


        if (intensidade > 0f)
        {
            streamIntensity =
                Mathf.Clamp01(
                    intensidade
                );
        }


        if (
            streamFullResponse.Length == 0 &&
            !string.IsNullOrWhiteSpace(reply)
        )
        {
            streamFullResponse.Append(
                reply
            );
        }


        streamFinished = true;


        Debug.Log(
            "[NYRA] Resposta completa recebida. " +
            "Emoção=" + streamEmotion +
            " | Intenção=" + streamIntent +
            " | Intensidade=" +
            streamIntensity.ToString("0.00")
        );
    }


    // ========================================================
    // VISÃO ESPONTÂNEA
    // ========================================================

    private IEnumerator ConsultarComentarioVisual()
    {
        if (visionRequestRunning)
            yield break;


        visionRequestRunning = true;


        if (
            isNyraSpeaking ||
            isProcessing ||
            visionSpeaking
        )
        {
            if (visionDebugLogs)
            {
                Debug.Log(
                    "[VISÃO] Consulta ignorada: Nyra está ocupada."
                );
            }


            visionRequestRunning = false;

            yield break;
        }


        using (
            UnityWebRequest request =
                UnityWebRequest.Get(
                    visionCommentUrl
                )
        )
        {
            request.timeout = 10;


            if (visionDebugLogs)
            {
                Debug.Log(
                    "[VISÃO] Consultando comentário espontâneo."
                );
            }


            yield return request.SendWebRequest();


            if (
                request.result !=
                UnityWebRequest.Result.Success
            )
            {
                if (visionDebugLogs)
                {
                    Debug.LogWarning(
                        "[VISÃO] Erro consultando Node: " +
                        request.error
                    );
                }


                visionRequestRunning = false;

                yield break;
            }


            string json =
                request.downloadHandler.text;


            if (string.IsNullOrWhiteSpace(json))
            {
                visionRequestRunning = false;

                yield break;
            }


            VisionCommentResponse response = null;


            try
            {
                response =
                    JsonUtility.FromJson<VisionCommentResponse>(
                        json
                    );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[VISÃO] JSON inválido: " +
                    e.Message
                );
            }


            if (response == null)
            {
                visionRequestRunning = false;

                yield break;
            }


            string comentario =
                ObterTextoComentarioVisual(
                    response
                );


            if (
                string.IsNullOrWhiteSpace(
                    comentario
                )
            )
            {
                if (visionDebugLogs)
                {
                    Debug.Log(
                        "[VISÃO] Nenhum comentário pendente."
                    );
                }


                visionRequestRunning = false;

                yield break;
            }


            if (
                Time.time <
                nextVisionSpeechTime
            )
            {
                if (visionDebugLogs)
                {
                    Debug.Log(
                        "[VISÃO] Comentário recebido, " +
                        "mas cooldown ainda ativo."
                    );
                }


                visionRequestRunning = false;

                yield break;
            }


            if (
                isProcessing ||
                isNyraSpeaking
            )
            {
                visionRequestRunning = false;

                yield break;
            }


            string emocao =
                ObterEmocaoVisual(
                    response
                );


            string intencao =
                ObterIntencaoVisual(
                    response
                );


            float intensidade =
                ObterIntensidadeVisual(
                    response
                );


            if (visionDebugLogs)
            {
                Debug.Log(
                    "[VISÃO] Nyra decidiu comentar. " +
                    "Emoção=" + emocao +
                    " | Intenção=" + intencao +
                    " | Intensidade=" +
                    intensidade.ToString("0.00")
                );
            }


            if (
                isProcessing &&
                !visionCanInterruptChat
            )
            {
                visionRequestRunning = false;

                yield break;
            }


            yield return StartCoroutine(
                FalarComentarioVisual(
                    comentario,
                    emocao,
                    intencao,
                    intensidade
                )
            );
        }


        visionRequestRunning = false;
    }


    // ========================================================
    // OBTER TEXTO DO COMENTÁRIO VISUAL
    // ========================================================

    private string ObterTextoComentarioVisual(
        VisionCommentResponse response
    )
    {
        if (response == null)
            return "";


        if (
            !string.IsNullOrWhiteSpace(
                response.comentario
            )
        )
        {
            return response.comentario.Trim();
        }


        if (
            !string.IsNullOrWhiteSpace(
                response.comment
            )
        )
        {
            return response.comment.Trim();
        }


        if (
            !string.IsNullOrWhiteSpace(
                response.reply
            )
        )
        {
            return response.reply.Trim();
        }


        if (
            !string.IsNullOrWhiteSpace(
                response.text
            )
        )
        {
            return response.text.Trim();
        }


        return "";
    }


    // ========================================================
    // OBTER EMOÇÃO VISUAL
    // ========================================================

    private string ObterEmocaoVisual(
        VisionCommentResponse response
    )
    {
        if (response == null)
            return "neutral";


        if (
            !string.IsNullOrWhiteSpace(
                response.emocao
            )
        )
        {
            return response.emocao;
        }


        if (
            !string.IsNullOrWhiteSpace(
                response.emotion
            )
        )
        {
            return response.emotion;
        }


        return "neutral";
    }


    // ========================================================
    // OBTER INTENÇÃO VISUAL
    // ========================================================

    private string ObterIntencaoVisual(
        VisionCommentResponse response
    )
    {
        if (response == null)
            return "assertion";


        if (
            !string.IsNullOrWhiteSpace(
                response.intencao
            )
        )
        {
            return response.intencao;
        }


        if (
            !string.IsNullOrWhiteSpace(
                response.intent
            )
        )
        {
            return response.intent;
        }


        return "assertion";
    }


    // ========================================================
    // OBTER INTENSIDADE VISUAL
    // ========================================================

    private float ObterIntensidadeVisual(
        VisionCommentResponse response
    )
    {
        if (response == null)
            return 0.35f;


        if (
            response.intensidade > 0f
        )
        {
            return Mathf.Clamp01(
                response.intensidade
            );
        }


        if (
            response.intensity > 0f
        )
        {
            return Mathf.Clamp01(
                response.intensity
            );
        }


        return 0.35f;
    }


    // ========================================================
    // FALAR COMENTÁRIO VISUAL
    // ========================================================

    private IEnumerator FalarComentarioVisual(
        string comentario,
        string emocao,
        string intencao,
        float intensidade
    )
    {
        if (
            azureTTS == null ||
            string.IsNullOrWhiteSpace(
                comentario
            )
        )
        {
            yield break;
        }


        if (
            isProcessing ||
            isNyraSpeaking
        )
        {
            yield break;
        }


        visionSpeaking = true;

        isNyraSpeaking = true;


        if (azureSTT != null)
        {
            try
            {
                azureSTT.StopListening();
            }
            catch
            {
            }
        }


        AddMessage(
            "Nyra",
            comentario
        );


        azureTTS.BeginStreaming(
            emocao,
            intencao,
            Mathf.Clamp01(
                intensidade
            )
        );


        azureTTS.EnqueuePhrase(
            comentario,
            emocao,
            intencao,
            Mathf.Clamp01(
                intensidade
            )
        );


        if (visionDebugLogs)
        {
            Debug.Log(
                "[VISÃO] Nyra falando espontaneamente. " +
                "Emoção=" + emocao +
                " | Intenção=" + intencao +
                " | Intensidade=" +
                intensidade.ToString("0.00")
            );
        }


        azureTTS.EndStreaming();


        yield return StartCoroutine(
            azureTTS.WaitForStreamingComplete()
        );


        isNyraSpeaking = false;

        visionSpeaking = false;


        nextVisionSpeechTime =
            Time.time +
            visionSpeechCooldown;


        voiceCooldownUntil =
            Time.time +
            0.8f;


        if (
            azureSTT != null &&
            !isProcessing
        )
        {
            yield return new WaitForSeconds(
                0.8f
            );


            try
            {
                azureSTT.StartListening();


                Debug.Log(
                    "[VISÃO] STT reativado."
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[VISÃO] Erro reativando STT: " +
                    e.Message
                );
            }
        }
    }


    // ========================================================
    // VOICE INPUT
    // ========================================================

    public void ReceiveVoiceInput(
        string text
    )
    {
        if (isNyraSpeaking)
        {
            Debug.Log(
                "Voz ignorada: Nyra está falando."
            );

            return;
        }


        if (
            Time.time <
            voiceCooldownUntil
        )
        {
            Debug.Log(
                "Voz ignorada: cooldown de áudio."
            );

            return;
        }


        if (isProcessing)
        {
            Debug.Log(
                "Voz ignorada: já existe uma requisição."
            );

            return;
        }


        if (string.IsNullOrWhiteSpace(text))
            return;


        string normalized =
            text.Trim()
                .ToLowerInvariant();


        if (
            normalized ==
                lastRecognizedText &&
            Time.time -
            lastRecognizedTime < 2f
        )
        {
            Debug.Log(
                "Voz duplicada ignorada."
            );

            return;
        }


        lastRecognizedText =
            normalized;


        lastRecognizedTime =
            Time.time;


        Debug.Log(
            "Entrada de voz aceita."
        );


        SendText(text);
    }


    // ========================================================
    // ADD MESSAGE
    // ========================================================

    public void AddMessage(
        string sender,
        string message
    )
    {
        if (chatDisplay != null)
        {
            chatDisplay.text +=
                $"\n<b>{EscaparRichText(sender)}:</b> " +
                EscaparRichText(message);


            Canvas.ForceUpdateCanvases();


            if (chatScrollRect != null)
            {
                chatScrollRect.verticalNormalizedPosition =
                    0f;
            }
        }
    }


    // ========================================================
    // ESCAPE DE RICH TEXT
    // ========================================================

    private string EscaparRichText(
        string text
    )
    {
        if (string.IsNullOrEmpty(text))
            return "";


        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }


    // ========================================================
    // START RECORDING
    // ========================================================

    public void StartRecording()
    {
        if (azureSTT == null)
        {
            Debug.LogError(
                "AzureSTTUnity não configurado no ChatManager."
            );

            return;
        }


        if (isNyraSpeaking)
        {
            Debug.Log(
                "Não é possível iniciar o STT enquanto Nyra fala."
            );

            return;
        }


        if (isProcessing)
        {
            Debug.Log(
                "Não é possível iniciar o STT durante processamento."
            );

            return;
        }


        azureSTT.StartListening();


        AddMessage(
            "Sistema",
            "Estou te ouvindo..."
        );
    }


    // ========================================================
    // STOP RECORDING
    // ========================================================

    public void StopRecording()
    {
        if (azureSTT == null)
            return;


        azureSTT.StopListening();


        AddMessage(
            "Sistema",
            "Processando sua fala..."
        );
    }


    // ========================================================
    // AZURE STT FINAL
    // ========================================================

    private void OnVoiceRecognized(
        string recognizedText
    )
    {
        Debug.Log(
            "[ChatManager] Texto recebido do Azure STT."
        );


        if (isNyraSpeaking)
        {
            Debug.Log(
                "STT ignorado: Nyra está falando."
            );

            return;
        }


        if (
            Time.time <
            voiceCooldownUntil
        )
        {
            Debug.Log(
                "STT ignorado: cooldown."
            );

            return;
        }


        if (isProcessing)
        {
            Debug.Log(
                "STT ignorado: sistema ocupado."
            );

            return;
        }


        if (
            string.IsNullOrWhiteSpace(
                recognizedText
            )
        )
        {
            Debug.LogWarning(
                "Azure retornou texto vazio."
            );

            return;
        }


        Debug.Log(
            "STT finalizado."
        );


        ReceiveVoiceInput(
            recognizedText
        );
    }


    // ========================================================
    // FOCUS LOGIN
    // ========================================================

    private void FocusLoginField()
    {
        if (userIdInputField == null)
            return;


        userIdInputField.Select();

        userIdInputField.ActivateInputField();
    }


    // ========================================================
    // FOCUS CHAT
    // ========================================================

    private void FocusChatField()
    {
        if (userInputField == null)
            return;


        userInputField.Select();

        userInputField.ActivateInputField();
    }


    // ========================================================
    // MANTER FOCO
    // ========================================================

    private void MaintainInputFocus()
    {
        if (isProcessing)
            return;


        if (
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current
                .currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current
                .currentSelectedGameObject
                .GetComponent<Button>() != null
        )
        {
            return;
        }


        // ========================================================
        // LOGIN
        // ========================================================

        if (
            loginPanel != null &&
            loginPanel.activeSelf &&
            userIdInputField != null &&
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current
                .currentSelectedGameObject !=
            userIdInputField.gameObject
        )
        {
            FocusLoginField();
        }


        // ========================================================
        // CHAT
        // ========================================================

        if (
            chatPanel != null &&
            chatPanel.activeSelf &&
            userInputField != null &&
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current
                .currentSelectedGameObject !=
            userInputField.gameObject
        )
        {
            FocusChatField();
        }
    }
}