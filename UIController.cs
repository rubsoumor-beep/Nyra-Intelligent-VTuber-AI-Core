using UnityEngine;
using TMPro;

public class UIController : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField userInput;
    public TMP_Text chatBox;
    public ChatManager chatManager;

    void Start()
    {
        // Garante que encontramos o ChatManager na cena
        if (chatManager == null)
        {
            chatManager = FindObjectOfType<ChatManager>();
            if (chatManager == null)
                Debug.LogError("❌ Nenhum ChatManager encontrado na cena!");
        }
    }

    public void OnSendButtonClicked()
    {
        if (chatManager == null || userInput == null) return;

        string text = userInput.text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        Debug.Log("📨 Enviando texto para backend: " + text);

        chatManager.SendText(text); // ✅ Agora funciona

        userInput.text = "";
    }

    public void AddMessageToChat(string msg)
    {
        if (chatBox != null)
            chatBox.text += msg + "\n";
    }
}
