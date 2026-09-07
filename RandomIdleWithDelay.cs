using UnityEngine;

public class RandomIdleAnimator : MonoBehaviour
{
    [Header("Animator")]
    public Animator animator;           // Animator do avatar

    [Header("Idle States")]
    public string[] idleStateNames;     // Nomes dos estados de Idle no Animator

    [Header("Delay Settings")]
    public float minDelay = 2f;         // Mínimo tempo entre idles
    public float maxDelay = 6f;         // Máximo tempo entre idles

    [Header("Transition Settings")]
    public float transitionDuration = 0.3f; // Duração do blend entre idles

    private float timer = 0f;
    private float currentDelay = 0f;

    void Start()
    {
        if (idleStateNames.Length == 0)
        {
            Debug.LogWarning("Nenhum Idle State definido!");
            return;
        }

        SetNextDelay();
        PlayRandomIdle();
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Pega a animação atual
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        // Só trocar se o delay passou e a animação atual terminou
        if (timer >= currentDelay && state.normalizedTime >= 1f)
        {
            PlayRandomIdle();
            SetNextDelay();
        }
    }

    void PlayRandomIdle()
    {
        int index = Random.Range(0, idleStateNames.Length);

        // Troca de estado com blend para suavizar a transição
        animator.CrossFade(idleStateNames[index], transitionDuration);

        timer = 0f;
    }

    void SetNextDelay()
    {
        // Delay aleatório, pode incluir o tempo da animação atual se quiser
        currentDelay = Random.Range(minDelay, maxDelay);
    }
}
