using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonPop : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        transform.localScale = originalScale * 0.92f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = originalScale;
    }
}