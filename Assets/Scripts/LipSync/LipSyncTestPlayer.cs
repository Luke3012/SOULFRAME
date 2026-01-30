using UnityEngine;

public class LipSyncTestPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip clip;

    public void Play()
    {
        if (!audioSource || !clip) return;
        audioSource.clip = clip;
        audioSource.Play();
    }
}
