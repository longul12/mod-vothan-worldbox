using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sakuga
{
    /// <summary>A compact, sortable world ranking for every living tiered fighter.</summary>
    internal static class RankWindow
    {
        private const int TopN = 100;
        private const float Width = 1040f;
        private const float Height = 760f;

        private struct Entry
        {
            public Actor Actor;
            public int Realm;
            public double Power;
            public int Kills;
            public float Sxp;
        }

        private static readonly List<Entry> _entries = new List<Entry>(128);
        private static readonly List<GameObject> _rows = new List<GameObject>(TopN);
        private static UnityEngine.Canvas _canvas;
        private static GameObject _root;
        private static RectTransform _content;
        private static Text _summary;

        [SakugaInit]
        private static void Init()
        {
            Hooks.WorldCleared += Hide;
        }

        public static void Show()
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }
                if (_root == null)
                {
                    return;
                }
                _root.SetActive(true);
                _root.transform.SetAsLastSibling();
                Refresh();
            }
            catch (Exception e)
            {
                SakugaMod.Report("rank window", e);
            }
        }

        public static void Hide()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private static void Build()
        {
            GameObject canvasGo = new GameObject("SakugaRankCanvas", typeof(UnityEngine.Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.GetComponent<UnityEngine.Canvas>();
            _canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("SakugaRankWindow", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(canvasGo.transform, false);
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(Width, Height);
            rootRt.anchoredPosition = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.04f, 0.98f);

            GameObject titleBar = Panel("TitleBar", rootRt, new Color(0.018f, 0.028f, 0.023f, 1f));
            RectTransform titleRt = titleBar.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -58f);
            titleRt.offsetMax = Vector2.zero;
            RankWindowDrag drag = titleBar.AddComponent<RankWindowDrag>();
            drag.Window = rootRt;

            Text title = Label("Title", titleRt, Locale.Get("sakuga_rank_title"), 25, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tr = title.rectTransform;
            tr.offsetMin = new Vector2(18f, 0f);
            tr.offsetMax = new Vector2(-210f, 0f);
            title.color = new Color(1f, 0.86f, 0.42f, 1f);

            Button refresh = TextButton("Refresh", titleRt, Locale.Get("sakuga_rank_refresh"), new Color(0.12f, 0.28f, 0.20f, 1f), Refresh);
            RectTransform rr = refresh.GetComponent<RectTransform>();
            rr.anchorMin = rr.anchorMax = new Vector2(1f, 0.5f);
            rr.pivot = new Vector2(1f, 0.5f);
            rr.anchoredPosition = new Vector2(-60f, 0f);
            rr.sizeDelta = new Vector2(130f, 38f);

            Button close = TextButton("Close", titleRt, "×", new Color(0.50f, 0.11f, 0.09f, 1f), Hide);
            RectTransform cr = close.GetComponent<RectTransform>();
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.anchoredPosition = new Vector2(-10f, 0f);
            cr.sizeDelta = new Vector2(40f, 38f);

            GameObject summaryGo = new GameObject("Summary", typeof(RectTransform), typeof(Text));
            summaryGo.transform.SetParent(rootRt, false);
            RectTransform sr = summaryGo.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 1f);
            sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(14f, -92f);
            sr.offsetMax = new Vector2(-14f, -62f);
            _summary = summaryGo.GetComponent<Text>();
            SetText(_summary, 15, TextAnchor.MiddleLeft, FontStyle.Normal);
            _summary.color = new Color(0.72f, 0.82f, 0.74f, 1f);

            CreateHeader(rootRt);
            CreateScroll(rootRt);
            _root.SetActive(false);
        }

        private static void CreateHeader(RectTransform root)
        {
            GameObject header = Panel("Header", root, new Color(0.10f, 0.16f, 0.13f, 1f));
            RectTransform rt = header.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(12f, -132f);
            rt.offsetMax = new Vector2(-12f, -96f);
            HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
            ConfigureRowLayout(layout);
            AddCell(header.transform, Locale.Get("sakuga_rank_no"), 62f, TextAnchor.MiddleCenter, true);
            AddCell(header.transform, Locale.Get("sakuga_rank_name"), 286f, TextAnchor.MiddleLeft, true);
            AddCell(header.transform, Locale.Get("sakuga_rank_realm"), 210f, TextAnchor.MiddleLeft, true);
            AddCell(header.transform, Locale.Get("sakuga_rank_power"), 150f, TextAnchor.MiddleRight, true);
            AddCell(header.transform, Locale.Get("sakuga_rank_kills"), 100f, TextAnchor.MiddleRight, true);
            AddCell(header.transform, Locale.Get("sakuga_rank_xp"), 170f, TextAnchor.MiddleRight, true);
        }

        private static void CreateScroll(RectTransform root)
        {
            GameObject scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(root, false);
            RectTransform rt = scrollGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -136f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.26f);

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            RectTransform vr = viewport.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero;
            vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero;
            vr.offsetMax = Vector2.zero;

            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport.transform, false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = vr;
            scroll.content = _content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
        }

        private static void Refresh()
        {
            if (_content == null)
            {
                return;
            }
            ClearRows();
            Collect();
            int shown = Mathf.Min(TopN, _entries.Count);
            for (int i = 0; i < shown; i++)
            {
                CreateRow(_entries[i], i);
            }
            if (_summary != null)
            {
                _summary.text = Locale.Format("sakuga_rank_summary", _entries.Count, shown);
            }
        }

        private static void Collect()
        {
            _entries.Clear();
            IReadOnlyList<Fighter> fighters = Fighters.All;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                Actor actor = fighter != null ? fighter.A : null;
                if (actor == null || actor.data == null || !actor.isAlive())
                {
                    continue;
                }
                int realm = RealmControl.Rank(actor);
                if (realm <= 0)
                {
                    continue;
                }
                _entries.Add(new Entry
                {
                    Actor = actor,
                    Realm = realm,
                    Power = CombatPower(actor),
                    Kills = actor.data.kills,
                    Sxp = Progression.Sxp(actor)
                });
            }
            _entries.Sort((a, b) =>
            {
                int realm = b.Realm.CompareTo(a.Realm);
                if (realm != 0) return realm;
                int power = b.Power.CompareTo(a.Power);
                if (power != 0) return power;
                int kills = b.Kills.CompareTo(a.Kills);
                if (kills != 0) return kills;
                return b.Sxp.CompareTo(a.Sxp);
            });
        }

        private static double CombatPower(Actor actor)
        {
            double tier = Math.Max(0.3, Tiers.Power(actor));
            double health = Math.Max(1, actor.getMaxHealth());
            double damage = Math.Max(1, actor.stats["damage"]);
            double armor = 1.0 + Math.Max(0, Math.Min(95, actor.stats["armor"])) / 100.0;
            double speed = 1.0 + Math.Max(0, actor.stats["speed"]) / 100.0;
            return tier * damage * Math.Sqrt(health) * armor * speed;
        }

        private static string RealmName(Entry entry)
        {
            if (entry.Realm > Tiers.Count)
            {
                return Locale.Get("trait_" + Primordials.TraitId);
            }
            return Tiers.Name((Tier)entry.Realm);
        }

        private static void CreateRow(Entry entry, int index)
        {
            GameObject row = new GameObject("RankRow" + index, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_content, false);
            LayoutElement le = row.GetComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            Image image = row.GetComponent<Image>();
            image.color = index % 2 == 0 ? new Color(0.045f, 0.075f, 0.06f, 0.90f) : new Color(0.075f, 0.11f, 0.09f, 0.90f);
            Button button = row.GetComponent<Button>();
            button.targetGraphic = image;
            Actor captured = entry.Actor;
            button.onClick.AddListener(() => OpenUnit(captured));
            ConfigureRowLayout(row.GetComponent<HorizontalLayoutGroup>());

            string rankColor = index == 0 ? "#FFD700" : index == 1 ? "#C0C0C0" : index == 2 ? "#CD7F32" : "#FFFFFF";
            AddCell(row.transform, "<color=" + rankColor + "><b>" + (index + 1) + "</b></color>", 62f, TextAnchor.MiddleCenter, false);
            AddCell(row.transform, entry.Actor.getName(), 286f, TextAnchor.MiddleLeft, false);
            Text realm = AddCell(row.transform, RealmName(entry), 210f, TextAnchor.MiddleLeft, false);
            realm.color = Tiers.LightColor(Tiers.Of(entry.Actor));
            AddCell(row.transform, FormatNumber(entry.Power), 150f, TextAnchor.MiddleRight, false);
            AddCell(row.transform, FormatNumber(entry.Kills), 100f, TextAnchor.MiddleRight, false);
            AddCell(row.transform, FormatNumber(entry.Sxp), 170f, TextAnchor.MiddleRight, false);
            _rows.Add(row);
        }

        private static void OpenUnit(Actor actor)
        {
            Hide();
            if (actor == null || !actor.isAlive())
            {
                return;
            }
            try
            {
                ActionLibrary.openUnitWindow(actor);
            }
            catch (Exception e)
            {
                SakugaMod.Report("rank unit", e);
            }
        }

        private static void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                {
                    UnityEngine.Object.Destroy(_rows[i]);
                }
            }
            _rows.Clear();
        }

        private static GameObject Panel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static Text Label(string name, Transform parent, string value, int size, TextAnchor alignment, FontStyle style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Text text = go.GetComponent<Text>();
            SetText(text, size, alignment, style);
            text.text = value;
            return text;
        }

        private static void SetText(Text text, int size, TextAnchor alignment, FontStyle style)
        {
            text.font = LocalizedTextManager.current_font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static Button TextButton(string name, Transform parent, string value, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            Label("Label", go.transform, value, value == "×" ? 25 : 16, TextAnchor.MiddleCenter, FontStyle.Bold);
            return button;
        }

        private static Text AddCell(Transform row, string value, float width, TextAnchor alignment, bool bold)
        {
            GameObject go = new GameObject("Cell", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(row, false);
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            Text text = go.GetComponent<Text>();
            SetText(text, bold ? 16 : 15, alignment, bold ? FontStyle.Bold : FontStyle.Normal);
            text.text = value;
            return text;
        }

        private static void ConfigureRowLayout(HorizontalLayoutGroup layout)
        {
            layout.padding = new RectOffset(8, 8, 0, 0);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
        }

        private static string FormatNumber(double value)
        {
            if (value >= 1000000000d) return (value / 1000000000d).ToString("0.##") + "B";
            if (value >= 1000000d) return (value / 1000000d).ToString("0.##") + "M";
            if (value >= 1000d) return (value / 1000d).ToString("0.##") + "K";
            return value.ToString("0");
        }
    }

    internal sealed class RankWindowDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform Window;
        private Vector2 _last;

        public void OnPointerDown(PointerEventData eventData)
        {
            RectTransform parent = Window != null ? Window.parent as RectTransform : null;
            if (parent != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out _last);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransform parent = Window != null ? Window.parent as RectTransform : null;
            if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 now))
            {
                return;
            }
            Window.anchoredPosition += now - _last;
            _last = now;
        }
    }
}
