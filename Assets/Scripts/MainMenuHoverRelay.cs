using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class MainMenuHoverRelay : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private MainMenuController controller;
    private bool isPointerOver;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponentInParent<MainMenuController>();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
        if (controller != null)
        {
            controller.SetHoverReaction(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isPointerOver)
        {
            return;
        }

        isPointerOver = false;
        if (controller != null)
        {
            controller.SetHoverReaction(false);
        }
    }

    private void OnDisable()
    {
        if (isPointerOver && controller != null)
        {
            controller.SetHoverReaction(false);
        }

        isPointerOver = false;
    }
}
