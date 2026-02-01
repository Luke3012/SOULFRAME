using System.Text;
using TMPro;
using UnityEngine;

public class UIHintBar : MonoBehaviour
{
    public enum HintIcon { Arrows, Enter, Backspace, Esc }

    [System.Serializable]
    public struct HintItem
    {
        public HintIcon icon;
        public string label;
        public HintItem(HintIcon icon, string label) { this.icon = icon; this.label = label; }
    }

    [Header("UI")]
    [SerializeField] private TMP_Text hintText;

    [Header("Sprite Assets (5 separati)")]
    [SerializeField] private TMP_SpriteAsset arrowsVerticalAsset;
    [SerializeField] private TMP_SpriteAsset arrowsHorizontalAsset;
    [SerializeField] private TMP_SpriteAsset enterAsset;       // questo è il BASE (quello con i fallback)
    [SerializeField] private TMP_SpriteAsset backspaceAsset;
    [SerializeField] private TMP_SpriteAsset escapeAsset;

    [Header("Formatting")]
    [SerializeField] private string separator = "   ";
    [SerializeField] private bool useFallbackTextIfMissing = true;

    private bool useHorizontalArrows = false;

    // cache nomi reali dentro gli asset (spesso finiscono con _0)
    private string arrowsVertName, arrowsHorizName, enterName, backspaceName, escName;

    public void SetArrowsHorizontal(bool horizontal) => useHorizontalArrows = horizontal;

    private void Awake() => EnsureTextSetup();
    private void OnEnable() => EnsureTextSetup();

    public void SetHints(string hints)
    {
        EnsureTextSetup();
        if (hintText != null) hintText.text = hints ?? string.Empty;
    }

    public void SetHints(params HintItem[] items)
    {
        EnsureTextSetup();
        if (hintText == null) return;

        if (items == null || items.Length == 0)
        {
            hintText.text = string.Empty;
            return;
        }

        var sb = new StringBuilder(128);

        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0) sb.Append(separator);

            string iconToken = BuildIconToken(items[i].icon);
            if (!string.IsNullOrEmpty(iconToken))
                sb.Append(iconToken).Append(' ');

            sb.Append(items[i].label);
        }

        hintText.text = sb.ToString();
    }

    private void EnsureTextSetup()
    {
        if (hintText == null) return;

        hintText.richText = true;

        // IMPORTANTISSIMO: TMP usa un SOLO spriteAsset "base"
        // e poi cerca negli eventuali fallback
        if (hintText.spriteAsset == null)
            hintText.spriteAsset = enterAsset;

        // cache nomi reali delle sprite (primo elemento della Sprite Character Table)
        arrowsVertName ??= GetFirstSpriteName(arrowsVerticalAsset);
        arrowsHorizName ??= GetFirstSpriteName(arrowsHorizontalAsset);
        enterName ??= GetFirstSpriteName(enterAsset);
        backspaceName ??= GetFirstSpriteName(backspaceAsset);
        escName ??= GetFirstSpriteName(escapeAsset);
    }

    private string BuildIconToken(HintIcon icon)
    {
        string spriteName = icon switch
        {
            HintIcon.Arrows => useHorizontalArrows ? arrowsHorizName : arrowsVertName,
            HintIcon.Enter => enterName,
            HintIcon.Backspace => backspaceName,
            HintIcon.Esc => escName,
            _ => null
        };

        if (!string.IsNullOrEmpty(spriteName))
        {
            return $"<sprite name=\"{spriteName}\" tint=1>";
        }

        if (!useFallbackTextIfMissing) return string.Empty;

        return icon switch
        {
            HintIcon.Arrows => "[ARROWS]",
            HintIcon.Enter => "[ENTER]",
            HintIcon.Backspace => "[BACKSPACE]",
            HintIcon.Esc => "[ESC]",
            _ => "[KEY]"
        };
    }

    private static string GetFirstSpriteName(TMP_SpriteAsset asset)
    {
        if (asset == null) return null;
        var table = asset.spriteCharacterTable;
        if (table != null && table.Count > 0 && table[0] != null)
            return table[0].name;   // spesso tipo "keyboard_enter_0"
        return null;
    }
}
