using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class GeminiAI : MonoBehaviour
{
    [Header("Gemini AI")]
    [Tooltip("Chave da API. NÃO coloque a chave real neste arquivo.")]
    public string BEARER_TOKEN = "";

    [Header("Configuração")]
    [SerializeField]
    private string model = "gemini-2.0-flash";

    private const string API_URL =
        "https://api.studio.generativeai.google/v1beta/models/";

    public IEnumerator AskGemini(
        string prompt,
        System.Action<string> OnResponse
    )
    {
        if (string.IsNullOrWhiteSpace(BEARER_TOKEN))
        {
            Debug.LogError(
                "❌ [NYRA] Gemini API Key não configurada."
            );

            OnResponse?.Invoke(null);
            yield break;
        }

        string url =
            API_URL +
            model +
            ":generateContent";

        string json =
            "{\"contents\": [{\"role\": \"user\", \"parts\": [{\"text\": \"" +
            EscapeJson(prompt) +
            "\"}]}]}";

        using (UnityWebRequest req =
            new UnityWebRequest(url, "POST"))
        {
            byte[] body =
                Encoding.UTF8.GetBytes(json);

            req.uploadHandler =
                new UploadHandlerRaw(body);

            req.downloadHandler =
                new DownloadHandlerBuffer();

            req.SetRequestHeader(
                "Content-Type",
                "application/json"
            );

            req.SetRequestHeader(
                "Authorization",
                "Bearer " + BEARER_TOKEN
            );

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    "❌ [NYRA] Erro Gemini: " +
                    req.error
                );

                Debug.LogError(
                    "Resposta: " +
                    req.downloadHandler.text
                );

                OnResponse?.Invoke(null);
            }
            else
            {
                string result =
                    req.downloadHandler.text;

                string responseText =
                    ExtractText(result);

                OnResponse?.Invoke(
                    responseText
                );
            }
        }
    }

    private string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";

        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private string ExtractText(string json)
    {
        int idx =
            json.IndexOf("\"text\":");

        if (idx == -1)
            return json;

        idx =
            json.IndexOf(
                "\"",
                idx + 7
            ) + 1;

        if (idx <= 0)
            return json;

        int end =
            json.IndexOf(
                "\"",
                idx
            );

        if (end == -1)
            return json;

        return json.Substring(
            idx,
            end - idx
        );
    }
}