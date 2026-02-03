using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIIntroSequence : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float zoomDistance = 4f;
    [SerializeField] private float zoomDuration = 1.2f;

    [Header("Title Fade")]
    [SerializeField] private CanvasGroup titleGroup;
    [SerializeField] private float titleFadeDelay = 0.15f;
    [SerializeField] private float titleFadeDuration = 0.6f;

    [Header("Fade extra objects (buttons)")]
    [SerializeField] private List<GameObject> fadeInObjects = new List<GameObject>();

    private readonly List<CanvasGroup> extraGroups = new List<CanvasGroup>();

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (titleGroup != null)
        {
            titleGroup.alpha = 0f;
            titleGroup.interactable = false;
            titleGroup.blocksRaycasts = false;
        }

        extraGroups.Clear();
        foreach (var go in fadeInObjects)
        {
            if (go == null) continue;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
            extraGroups.Add(cg);
        }
    }

    private void Start()
    {
        if (targetCamera == null) return;
        StartCoroutine(PlayIntro());
    }

    private IEnumerator PlayIntro()
    {
        Vector3 targetPosition = targetCamera.transform.position;
        Quaternion targetRotation = targetCamera.transform.rotation;
        Vector3 startPosition = targetPosition - targetCamera.transform.forward * zoomDistance;

        targetCamera.transform.position = startPosition;
        targetCamera.transform.rotation = targetRotation;

        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = zoomDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / zoomDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            targetCamera.transform.position = Vector3.LerpUnclamped(startPosition, targetPosition, eased);
            yield return null;
        }

        targetCamera.transform.position = targetPosition;

        if (titleFadeDelay > 0f)
            yield return new WaitForSecondsRealtime(titleFadeDelay);

        float fadeElapsed = 0f;
        while (fadeElapsed < titleFadeDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = titleFadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / titleFadeDuration);

            if (titleGroup != null) titleGroup.alpha = t;
            for (int i = 0; i < extraGroups.Count; i++)
                extraGroups[i].alpha = t;

            yield return null;
        }

        if (titleGroup != null)
        {
            titleGroup.alpha = 1f;
            titleGroup.interactable = true;
            titleGroup.blocksRaycasts = true;
        }

        for (int i = 0; i < extraGroups.Count; i++)
        {
            extraGroups[i].alpha = 1f;
            extraGroups[i].interactable = true;
            extraGroups[i].blocksRaycasts = true;
        }
    }
}
