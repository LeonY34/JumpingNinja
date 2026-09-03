using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JumpingNinja
{
    internal static class RuntimeUi
    {
        public const float PortraitAspect = 9f / 16f;

        private static Font cachedFont;

        public static readonly Color Ink = new Color(0.055f, 0.067f, 0.09f, 1f);
        public static readonly Color Paper = new Color(0.96f, 0.965f, 0.94f, 1f);
        public static readonly Color Red = new Color(0.86f, 0.18f, 0.14f, 1f);
        public static readonly Color Muted = new Color(0.33f, 0.38f, 0.43f, 1f);

        private static Font GameFont
        {
            get
            {
                if (cachedFont == null)
                {
                    cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return cachedFont;
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder = 0)
        {
            GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = PortraitViewport.ShouldLetterbox
                ? CanvasScaler.ScreenMatchMode.Expand
                : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject contentObject = new GameObject("Portrait Content", typeof(RectTransform));
            contentObject.transform.SetParent(canvasObject.transform, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            Stretch(content);
            if (PortraitViewport.ShouldLetterbox)
            {
                AspectRatioFitter fitter = contentObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = PortraitAspect;
            }

            PortraitCanvasRoot canvasRoot = canvasObject.AddComponent<PortraitCanvasRoot>();
            canvasRoot.Content = content;
            return canvas;
        }

        public static Transform Content(Canvas canvas)
        {
            PortraitCanvasRoot root = canvas.GetComponent<PortraitCanvasRoot>();
            return root != null && root.Content != null ? root.Content : canvas.transform;
        }

        public static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateText(
            string name,
            Transform parent,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color,
            FontStyle style = FontStyle.Normal)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            Text text = gameObject.GetComponent<Text>();
            text.font = GameFont;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = style;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label, UnityAction onClick, Color color)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            gameObject.transform.SetParent(parent, false);

            Image image = gameObject.GetComponent<Image>();
            image.color = color;

            Button button = gameObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            Outline outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.82f, 0.24f, 1f);
            outline.effectDistance = new Vector2(7f, -7f);
            outline.useGraphicAlpha = false;
            outline.enabled = false;
            gameObject.AddComponent<ButtonSelectionIndicator>().Initialize(outline);
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            Text buttonText = CreateText("Label", gameObject.transform, label, 46, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            Stretch(buttonText.rectTransform);
            return button;
        }

        public static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }

        public static InputField CreateInputField(string name, Transform parent, string placeholder)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            gameObject.transform.SetParent(parent, false);
            Image image = gameObject.GetComponent<Image>();
            image.color = Color.white;

            Text valueText = CreateText("Value", gameObject.transform, string.Empty, 44, TextAnchor.MiddleLeft, Ink);
            SetOffsets(valueText.rectTransform, new Vector2(32f, 12f), new Vector2(-32f, -12f));

            Text placeholderText = CreateText("Placeholder", gameObject.transform, placeholder, 40, TextAnchor.MiddleLeft, new Color(0.4f, 0.45f, 0.5f, 0.75f));
            placeholderText.fontStyle = FontStyle.Italic;
            SetOffsets(placeholderText.rectTransform, new Vector2(32f, 12f), new Vector2(-32f, -12f));

            InputField input = gameObject.GetComponent<InputField>();
            input.textComponent = valueText;
            input.placeholder = placeholderText;
            input.characterLimit = 16;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = InputField.ContentType.Standard;
            return input;
        }

        public static InputField CreatePasswordField(string name, Transform parent, string placeholder)
        {
            InputField input = CreateInputField(name, parent, placeholder);
            input.characterLimit = 72;
            input.contentType = InputField.ContentType.Password;
            return input;
        }

        public static ScrollRect CreateScrollView(
            string name,
            Transform parent,
            Vector2 size,
            Vector2 position,
            out Transform content)
        {
            GameObject scrollObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;

            Image background = scrollObject.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);
            background.raycastTarget = true;

            GameObject viewportObject = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            Stretch(viewport);
            scroll.viewport = viewport;

            GameObject contentObject = new GameObject(
                "Content",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 14f;
            layout.padding = new RectOffset(12, 12, 16, 16);

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            scroll.verticalNormalizedPosition = 1f;
            Place(scrollObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), size, position);
            content = contentObject.transform;
            return scroll;
        }

        public static void AddLayoutHeight(GameObject gameObject, float preferredHeight)
        {
            LayoutElement layout = gameObject.GetComponent<LayoutElement>()
                ?? gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            layout.minHeight = preferredHeight;
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static void SetOffsets(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        public static void AddShadow(Graphic graphic, Color color, Vector2 distance)
        {
            Shadow shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }
    }

    internal sealed class PortraitCanvasRoot : MonoBehaviour
    {
        public RectTransform Content { get; set; }
    }

    internal sealed class ButtonSelectionIndicator : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Outline outline;

        public void Initialize(Outline selectionOutline)
        {
            outline = selectionOutline;
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (outline != null)
            {
                outline.enabled = true;
            }
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (outline != null)
            {
                outline.enabled = false;
            }
        }
    }
}
