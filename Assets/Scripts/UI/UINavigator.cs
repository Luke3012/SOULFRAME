using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UINavigator : MonoBehaviour
{
    public enum AxisMode
    {
        Vertical,
        Horizontal,
        Both
    }

    [Header("Navigation")]
    [SerializeField] private AxisMode axisMode = AxisMode.Vertical;
    [SerializeField] private bool wrapAround = true;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip moveClip;
    [SerializeField] private AudioClip confirmClip;
    [SerializeField] private AudioClip backClip;
    [SerializeField, Range(0.01f, 0.5f)] private float audioCooldown = 0.08f;

    private readonly List<Selectable> items = new List<Selectable>();
    private int currentIndex;
    private float lastAudioTime;
    private bool allowExit;
    private System.Action backAction;
    private System.Action exitAction;
    private UIFlowController uiFlowController;

    public void SetActions(System.Action onBack, System.Action onExit)
    {
        backAction = onBack;
        exitAction = onExit;
    }

    public void SetExitAllowed(bool allowed)
    {
        allowExit = allowed;
    }

    private void Awake()
    {
        uiFlowController = FindFirstObjectByType<UIFlowController>();
    }

    public void SetMenu(List<Selectable> selectables, AxisMode axis, bool resetIndex = true)
    {
        axisMode = axis;
        items.Clear();
        if (selectables != null)
        {
            foreach (var selectable in selectables)
            {
                if (selectable != null)
                {
                    items.Add(selectable);
                }
            }
        }

        if (resetIndex)
        {
            currentIndex = 0;
        }

        EnsureValidSelection(resetIndex);
    }

    private void Update()
    {
        if (uiFlowController != null && uiFlowController.IsWebOverlayOpen)
        {
            return;
        }

        // FIX: Gestisci Back/Exit PRIMA del check items (permette Backspace da stati senza selectables)
        if (HandleBack())
        {
            return;
        }

        if (items.Count == 0)
        {
            return;
        }

        if (HandleConfirm())
        {
            return;
        }

        HandleMove();
    }

    private bool HandleConfirm()
    {
        if (!Input.GetKeyDown(KeyCode.Return))
        {
            return false;
        }

        var selectable = GetCurrentSelectable();
        if (selectable == null)
        {
            return false;
        }

        var button = selectable.GetComponent<Button>();
        if (button != null)
        {
            PlayAudio(confirmClip);
            button.onClick.Invoke();
            return true;
        }

        if (EventSystem.current != null)
        {
            PlayAudio(confirmClip);
            ExecuteEvents.Execute(selectable.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            return true;
        }

        return false;
    }

    private bool HandleBack()
    {
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            PlayAudio(backClip);
            backAction?.Invoke();
            return true;
        }

        if (Input.GetKeyDown(KeyCode.Escape) && allowExit)
        {
            PlayAudio(backClip);
            exitAction?.Invoke();
            return true;
        }

        return false;
    }

    private void HandleMove()
    {
        int direction = 0;

        if (axisMode == AxisMode.Vertical || axisMode == AxisMode.Both)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                direction = -1;
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                direction = 1;
            }
        }

        if (direction == 0 && (axisMode == AxisMode.Horizontal || axisMode == AxisMode.Both))
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                direction = -1;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                direction = 1;
            }
        }

        if (direction == 0)
        {
            return;
        }

        int nextIndex = FindNextIndex(direction);
        if (nextIndex == currentIndex)
        {
            return;
        }

        currentIndex = nextIndex;
        SelectCurrent(true);
        PlayAudio(moveClip);
    }

    private int FindNextIndex(int direction)
    {
        if (items.Count == 0)
        {
            return currentIndex;
        }

        int startIndex = currentIndex;
        int index = currentIndex;

        for (int i = 0; i < items.Count; i++)
        {
            index += direction;

            if (wrapAround)
            {
                if (index < 0)
                {
                    index = items.Count - 1;
                }
                else if (index >= items.Count)
                {
                    index = 0;
                }
            }
            else
            {
                index = Mathf.Clamp(index, 0, items.Count - 1);
            }

            var selectable = items[index];
            if (selectable != null && selectable.IsInteractable() && selectable.gameObject.activeInHierarchy)
            {
                return index;
            }

            if (!wrapAround && index == startIndex)
            {
                break;
            }
        }

        return startIndex;
    }

    private Selectable GetCurrentSelectable()
    {
        if (items.Count == 0)
        {
            return null;
        }

        if (currentIndex < 0 || currentIndex >= items.Count)
        {
            currentIndex = 0;
        }

        return items[currentIndex];
    }

    private void EnsureValidSelection(bool force)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (force)
        {
            currentIndex = FindFirstInteractableIndex();
            SelectCurrent(true);
            return;
        }

        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null || !IsInMenu(selected))
        {
            currentIndex = FindFirstInteractableIndex();
            SelectCurrent(true);
        }
    }

    private int FindFirstInteractableIndex()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && items[i].IsInteractable() && items[i].gameObject.activeInHierarchy)
            {
                return i;
            }
        }

        return 0;
    }

    private bool IsInMenu(GameObject selected)
    {
        foreach (var selectable in items)
        {
            if (selectable != null && selectable.gameObject == selected)
            {
                return true;
            }
        }

        return false;
    }

    private void SelectCurrent(bool force)
    {
        var selectable = GetCurrentSelectable();
        if (selectable == null)
        {
            return;
        }

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }

        if (force)
        {
            selectable.Select();
        }
    }

    private void PlayAudio(AudioClip clip)
    {
        if (clip == null || audioSource == null)
        {
            return;
        }

        if (Time.unscaledTime - lastAudioTime < audioCooldown)
        {
            return;
        }

        lastAudioTime = Time.unscaledTime;
        audioSource.PlayOneShot(clip, 0.4f);
    }
}
