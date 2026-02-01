using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SelectableBlink : MonoBehaviour
{
    [Header("Blink")]
    [SerializeField] private readonly Color blinkColor = new Color(0.3f, 0.85f, 1f, 1f);
    [SerializeField] private readonly float blinkSpeed = 2.2f;
    [SerializeField] private readonly float minAlpha = 0.35f;
    [SerializeField] private readonly float maxAlpha = 1f;
    [SerializeField] private readonly bool onlyWhenSelected = true;
    [SerializeField] private Selectable targetSelectable;

    [Header("Glow (TMP)")]
    [SerializeField] private readonly bool enableTmpGlow = true;
    [SerializeField] private readonly float glowPower = 0.6f;
    [SerializeField] private readonly float glowInner = 0.06f;
    [SerializeField] private readonly float glowOuter = 0.15f;

    private TMP_Text tmpText;
    private Graphic uiGraphic;
    private Color baseColor;
    private Material tmpMaterialInstance;
    private float originalGlowPower;
    private float originalGlowInner;
    private float originalGlowOuter;
    private Color originalGlowColor;

    private void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        uiGraphic = GetComponent<Graphic>();
        baseColor = tmpText != null ? tmpText.color : uiGraphic != null ? uiGraphic.color : Color.white;

        if (tmpText != null)
        {
            tmpMaterialInstance = tmpText.fontMaterial;
            if (tmpMaterialInstance != null && HasGlowProperties(tmpMaterialInstance))
            {
                originalGlowPower = tmpMaterialInstance.GetFloat(ShaderUtilities.ID_GlowPower);
                originalGlowInner = tmpMaterialInstance.GetFloat(ShaderUtilities.ID_GlowInner);
                originalGlowOuter = tmpMaterialInstance.GetFloat(ShaderUtilities.ID_GlowOuter);
                originalGlowColor = tmpMaterialInstance.GetColor(ShaderUtilities.ID_GlowColor);
            }
        }

        if (targetSelectable == null)
        {
            targetSelectable = GetComponentInParent<Selectable>();
        }
    }

    private void OnDisable()
    {
        RestoreBaseColor();
    }

    private void Update()
    {
        bool shouldBlink = !onlyWhenSelected || IsSelected();
        if (!shouldBlink)
        {
            RestoreBaseColor();
            return;
        }

        float t = Mathf.Sin(Time.unscaledTime * blinkSpeed) * 0.5f + 0.5f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        Color targetColor = Color.Lerp(baseColor, blinkColor, t);
        targetColor.a = alpha;

        if (tmpText != null)
        {
            tmpText.color = targetColor;
            ApplyTmpGlow(alpha);
        }
        else if (uiGraphic != null)
        {
            uiGraphic.color = targetColor;
        }
    }

    private bool IsSelected()
    {
        if (EventSystem.current == null)
        {
            return true;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null)
        {
            return false;
        }

        if (targetSelectable != null)
        {
            return selected == targetSelectable.gameObject;
        }

        return selected == gameObject;
    }

    private void RestoreBaseColor()
    {
        if (tmpText != null)
        {
            tmpText.color = baseColor;
            if (tmpMaterialInstance != null && HasGlowProperties(tmpMaterialInstance))
            {
                tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowPower, originalGlowPower);
                tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowInner, originalGlowInner);
                tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowOuter, originalGlowOuter);
                tmpMaterialInstance.SetColor(ShaderUtilities.ID_GlowColor, originalGlowColor);
            }
        }
        else if (uiGraphic != null)
        {
            uiGraphic.color = baseColor;
        }
    }

    private void ApplyTmpGlow(float alpha)
    {
        if (!enableTmpGlow || tmpMaterialInstance == null || !HasGlowProperties(tmpMaterialInstance))
        {
            return;
        }

        tmpMaterialInstance.SetColor(ShaderUtilities.ID_GlowColor, new Color(blinkColor.r, blinkColor.g, blinkColor.b, alpha));
        tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowPower, glowPower);
        tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowInner, glowInner);
        tmpMaterialInstance.SetFloat(ShaderUtilities.ID_GlowOuter, glowOuter);
    }

    private bool HasGlowProperties(Material material)
    {
        return material.HasProperty(ShaderUtilities.ID_GlowPower) &&
               material.HasProperty(ShaderUtilities.ID_GlowInner) &&
               material.HasProperty(ShaderUtilities.ID_GlowOuter) &&
               material.HasProperty(ShaderUtilities.ID_GlowColor);
    }
}