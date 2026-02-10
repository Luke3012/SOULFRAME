using System.Text;
using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class UIHintBar : MonoBehaviour
{
    public enum HintIcon { Arrows, Enter, Backspace, Esc, Space, Delete, Any, Ins }
    public enum InspectorContext
    {
        DesktopOnly,
        TouchOnly,
        Both
    }
    public enum TouchSprite
    {
        None,
        Back,
        Keyboard,
        Confirm,
        Cancel,
        MicIdle,
        MicActive,
        Delete,
        Restore,
        Tap,
        Hold,
        HoldActive,
        SwipeHorizontal,
        SwipeVertical
    }

    [System.Serializable]
    public struct HintItem
    {
        public HintIcon icon;
        public string label;
        public HintItem(HintIcon icon, string label) { this.icon = icon; this.label = label; }
    }

    [System.Serializable]
    public struct TouchHintItem
    {
        public TouchSprite icon;
        public string label;

        public TouchHintItem(TouchSprite icon, string label)
        {
            this.icon = icon;
            this.label = label;
        }
    }

    [Header("UI")]
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private InspectorContext inspectorContext = InspectorContext.Both;

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

    [Header("Touch Sprite Assets")]
    [SerializeField] private TMP_SpriteAsset touchBackAsset;
    [SerializeField] private TMP_SpriteAsset touchKeyboardAsset;
    [SerializeField] private TMP_SpriteAsset touchConfirmAsset;
    [SerializeField] private TMP_SpriteAsset touchCancelAsset;
    [SerializeField] private TMP_SpriteAsset touchMicIdleAsset;
    [SerializeField] private TMP_SpriteAsset touchMicActiveAsset;
    [SerializeField] private TMP_SpriteAsset touchDeleteAsset;
    [SerializeField] private TMP_SpriteAsset touchRestoreAsset;
    [SerializeField] private TMP_SpriteAsset touchTapAsset;
    [SerializeField] private TMP_SpriteAsset touchHoldAsset;
    [SerializeField] private TMP_SpriteAsset touchHoldActiveAsset;
    [SerializeField] private TMP_SpriteAsset touchSwipeHorizontalAsset;
    [SerializeField] private TMP_SpriteAsset touchSwipeVerticalAsset;

    [Header("Formatting")]
    [SerializeField] private string separator = "   ";
    [SerializeField] private bool useFallbackTextIfMissing = true;

    private bool useHorizontalArrows = false;

    // Memorizziamo in cache i nomi reali negli asset (spesso finiscono con _0).
    private string arrowsVertName, arrowsHorizName, enterName, backspaceName, escName, spaceName, spaceOutlinedName, deleteName, anyName, insName;
    private bool spacePressed;
    private TMP_SpriteAsset touchRuntimeSpriteAsset;
    private readonly Dictionary<TMP_SpriteAsset, string> touchSpriteNameCache = new Dictionary<TMP_SpriteAsset, string>();

    public void SetArrowsHorizontal(bool horizontal) => useHorizontalArrows = horizontal;
    public void SetSpacePressed(bool pressed) => spacePressed = pressed;
    public InspectorContext CurrentInspectorContext => inspectorContext;
    public TMP_SpriteAsset GetTouchSpriteAsset(TouchSprite sprite)
    {
        return sprite switch
        {
            TouchSprite.Back => touchBackAsset,
            TouchSprite.Keyboard => touchKeyboardAsset,
            TouchSprite.Confirm => touchConfirmAsset,
            TouchSprite.Cancel => touchCancelAsset,
            TouchSprite.MicIdle => touchMicIdleAsset,
            TouchSprite.MicActive => touchMicActiveAsset,
            TouchSprite.Delete => touchDeleteAsset,
            TouchSprite.Restore => touchRestoreAsset,
            TouchSprite.Tap => touchTapAsset,
            TouchSprite.Hold => touchHoldAsset,
            TouchSprite.HoldActive => touchHoldActiveAsset,
            TouchSprite.SwipeHorizontal => touchSwipeHorizontalAsset,
            TouchSprite.SwipeVertical => touchSwipeVerticalAsset,
            _ => null
        };
    }

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

    public void SetTouchHints(params TouchHintItem[] items)
    {
        EnsureTextSetup();
        if (hintText == null) return;

        if (items == null || items.Length == 0)
        {
            hintText.text = string.Empty;
            return;
        }

        EnsureTouchSpriteSetup(items);
        var sb = new StringBuilder(192);
        for (int i = 0; i < items.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            string iconToken = BuildTouchIconToken(items[i].icon);
            if (!string.IsNullOrEmpty(iconToken))
            {
                sb.Append(iconToken).Append(' ');
            }

            sb.Append(items[i].label ?? string.Empty);
        }

        hintText.text = sb.ToString();
    }

    private void EnsureTextSetup()
    {
        if (hintText == null)
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].name.IndexOf("hint", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hintText = candidates[i];
                    break;
                }
            }

            if (hintText == null && candidates.Length > 0)
            {
                hintText = candidates[0];
            }
        }

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

    private void EnsureTouchSpriteSetup(TouchHintItem[] items)
    {
        if (hintText == null || items == null || items.Length == 0)
        {
            return;
        }

        if (touchRuntimeSpriteAsset == null)
        {
            TMP_SpriteAsset baseAsset = null;
            for (int i = 0; i < items.Length; i++)
            {
                TMP_SpriteAsset itemAsset = GetTouchSpriteAsset(items[i].icon);
                if (itemAsset != null)
                {
                    baseAsset = itemAsset;
                    break;
                }
            }

            if (baseAsset != null)
            {
                touchRuntimeSpriteAsset = Instantiate(baseAsset);
                touchRuntimeSpriteAsset.name = $"{baseAsset.name}_TouchHintRuntime";
                touchRuntimeSpriteAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            }
        }

        if (touchRuntimeSpriteAsset == null)
        {
            return;
        }

        for (int i = 0; i < items.Length; i++)
        {
            AddTouchFallback(touchRuntimeSpriteAsset, GetTouchSpriteAsset(items[i].icon));
        }

        hintText.spriteAsset = touchRuntimeSpriteAsset;
        hintText.richText = true;
        hintText.tintAllSprites = true;
    }

    private static void AddTouchFallback(TMP_SpriteAsset baseAsset, TMP_SpriteAsset fallback)
    {
        if (baseAsset == null || fallback == null || baseAsset == fallback)
        {
            return;
        }

        if (baseAsset.fallbackSpriteAssets == null)
        {
            baseAsset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
        }

        if (!baseAsset.fallbackSpriteAssets.Contains(fallback))
        {
            baseAsset.fallbackSpriteAssets.Add(fallback);
        }
    }

    private string BuildTouchIconToken(TouchSprite icon)
    {
        TMP_SpriteAsset iconAsset = GetTouchSpriteAsset(icon);
        string spriteName = GetTouchSpriteName(iconAsset);
        if (string.IsNullOrEmpty(spriteName))
        {
            return string.Empty;
        }

        return $"<sprite name=\"{spriteName}\" tint=1>";
    }

    private string GetTouchSpriteName(TMP_SpriteAsset iconAsset)
    {
        if (iconAsset == null)
        {
            return null;
        }

        if (touchSpriteNameCache.TryGetValue(iconAsset, out var cached))
        {
            return cached;
        }

        string spriteName = GetFirstSpriteName(iconAsset);
        touchSpriteNameCache[iconAsset] = spriteName;
        return spriteName;
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
