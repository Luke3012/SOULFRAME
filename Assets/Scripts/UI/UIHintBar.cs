using System.Text;
using TMPro;
using UnityEngine;

public class UIHintBar : MonoBehaviour
{
    public enum HintIcon { Arrows, Enter, Backspace, Esc, Space, Delete, Any, Ins }

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
    [SerializeField] private TMP_SpriteAsset spaceAsset;
    [SerializeField] private TMP_SpriteAsset spaceOutlinedAsset;
    [SerializeField] private TMP_SpriteAsset deleteAsset;
    [SerializeField] private TMP_SpriteAsset anyAsset;
    [SerializeField] private TMP_SpriteAsset insAsset;

    [Header("Formatting")]
    [SerializeField] private string separator = "   ";
    [SerializeField] private bool useFallbackTextIfMissing = true;

    private bool useHorizontalArrows = false;

    // Memorizziamo in cache i nomi reali negli asset (spesso finiscono con _0).
    private string arrowsVertName, arrowsHorizName, enterName, backspaceName, escName, spaceName, spaceOutlinedName, deleteName, anyName, insName;
    private bool spacePressed;

    public void SetArrowsHorizontal(bool horizontal) => useHorizontalArrows = horizontal;
    public void SetSpacePressed(bool pressed) => spacePressed = pressed;

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

        // In TMP usiamo un solo spriteAsset "base".
        // e poi cerca negli eventuali fallback
        if (hintText.spriteAsset == null)
            hintText.spriteAsset = enterAsset;

        // Memorizziamo in cache i nomi reali delle sprite (primo elemento della tabella Sprite Character)
        arrowsVertName ??= GetFirstSpriteName(arrowsVerticalAsset);
        arrowsHorizName ??= GetFirstSpriteName(arrowsHorizontalAsset);
        enterName ??= GetFirstSpriteName(enterAsset);
        backspaceName ??= GetFirstSpriteName(backspaceAsset);
        escName ??= GetFirstSpriteName(escapeAsset);
        spaceName ??= GetFirstSpriteName(spaceAsset);
        spaceOutlinedName ??= GetFirstSpriteName(spaceOutlinedAsset);
        deleteName ??= GetFirstSpriteName(deleteAsset);
        anyName ??= GetFirstSpriteName(anyAsset);
        insName ??= GetFirstSpriteName(insAsset);
    }

    private string BuildIconToken(HintIcon icon)
    {
        string spaceSprite = spacePressed && !string.IsNullOrEmpty(spaceOutlinedName) ? spaceOutlinedName : spaceName;
        string spriteName = icon switch
        {
            HintIcon.Arrows => useHorizontalArrows ? arrowsHorizName : arrowsVertName,
            HintIcon.Enter => enterName,
            HintIcon.Backspace => backspaceName,
            HintIcon.Esc => escName,
            HintIcon.Space => spaceSprite,
            HintIcon.Delete => deleteName,
            HintIcon.Any => anyName,
            HintIcon.Ins => insName,
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
            HintIcon.Space => spacePressed ? "[SPACE_OUTLINED]" : "[SPACE]",
            HintIcon.Delete => "[DELETE]",
            HintIcon.Any => "[ANY]",
            HintIcon.Ins => "[INS]",
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
