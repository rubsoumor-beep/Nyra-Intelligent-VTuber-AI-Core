using UnityEngine;
using Live2D.Cubism.Core;

[RequireComponent(typeof(AudioSource))]
public class Live2DLipSyncWithAnimation : MonoBehaviour
{
    [Header("Live2D")]
    public CubismModel cubismModel;
    public int mouthParamIndex = 18;
    public float sensitivity = 50f;
    public float smoothSpeed = 15f;

    private AudioSource audioSource;
    private CubismParameter mouthParam;
    private float currentValue = 0f;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();

        if (cubismModel == null)
        {
            Debug.LogError("CubismModel não definido!");
            enabled = false;
            return;
        }

        CubismParameter[] parameters = cubismModel.Parameters;
        if (mouthParamIndex < 0 || mouthParamIndex >= parameters.Length)
        {
            Debug.LogError("Índice do parâmetro da boca inválido!");
            enabled = false;
            return;
        }

        mouthParam = parameters[mouthParamIndex];
    }

    void Update()
    {
        // Calcula o volume do áudio
        float targetValue = 0f;

        if (audioSource.isPlaying)
        {
            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
                sum += Mathf.Abs(samples[i]);

            float volume = sum / samples.Length;
            targetValue = Mathf.Clamp(volume * sensitivity, mouthParam.MinimumValue, mouthParam.MaximumValue);
        }

        // Suaviza o movimento
        currentValue = Mathf.Lerp(currentValue, targetValue, Time.deltaTime * smoothSpeed);
    }

    void LateUpdate()
    {
        // Aplica o valor no final do frame, sobrescrevendo animações
        if (mouthParam != null)
        {
            mouthParam.Value = currentValue;
        }
    }
}
