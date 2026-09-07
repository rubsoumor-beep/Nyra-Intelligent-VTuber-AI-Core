using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

[System.Serializable]
public class EmotionRequest
{
    public string text;
}

[System.Serializable]
public class EmotionResponse
{
    public string emotion;
    public float score;
    public string error; // caso o servidor retorne erro
}

public class EmotionClient : MonoBehaviour
{
    public string serverUrl = "http://127.0.0.1:5000/analyze"; // ajuste para seu IP se necessário
    public ChatManager chatManager; // arraste seu ChatManager aqui no inspector

    public void AnalyzeText(string text)
    {
        StartCoroutine(PostText(text));
    }

    private IEnumerator PostText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning("Texto vazio para análise de emoção.");
            yield break;
        }

        EmotionRequest requestObj = new EmotionRequest { text = text };
        string jsonData = JsonUtility.ToJson(requestObj);

        UnityWebRequest www = new UnityWebRequest(serverUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Erro ao conectar com o servidor de emoção: " + www.error);
            yield break;
        }

        string responseText = www.downloadHandler.text;
        EmotionResponse response = JsonUtility.FromJson<EmotionResponse>(responseText);

        if (!string.IsNullOrEmpty(response.error))
        {
            Debug.LogError("Erro do servidor de emoção: " + response.error);
            yield break;
        }

        Debug.Log($"Emoção detectada: {response.emotion} (score {response.score})");

        if (chatManager != null)
        {
            chatManager.AddMessage("Sistema", $"Emoção detectada: {response.emotion} (score {response.score:F2})");
        }
    }
}
