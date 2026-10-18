using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// UI Toolkit HUD of the tiger encounter (Art/UI/Tiger/TigerHud.uxml): forest reticle, white flash, showcase header,
/// hotspot tags that follow the tiger, and the info panel with a leader line to the selected point.
/// TigerEncounter drives it; all positions it receives are screen pixels (origin bottom-left, like Camera.WorldToScreenPoint).
[RequireComponent(typeof(UIDocument))]
public class TigerHud : MonoBehaviour
{
    static readonly Color Accent = new(0.47f, 0.94f, 1f);

    VisualElement root, forestLayer, showcaseLayer, reticleTag, flash, header, instructions, infoPanel, statList, scanFill, tagLayer;
    Label infoCode, infoTitle, infoSubtitle, infoBody, infoIndex, infoStatus;
    Button returnButton, backButton;
    ReticleGraphic reticle;
    LeaderGraphic leader;
    readonly List<VisualElement> tags = new();
    string bodyFull = ""; float bodyStart; bool infoShown;
    float scanStart;

    public event Action ReturnClicked, BackClicked;
    public event Action<int> PointClicked;
    readonly List<VisualElement> items = new();

    void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;
        forestLayer = root.Q("forestLayer"); showcaseLayer = root.Q("showcaseLayer");
        reticleTag = root.Q("reticleTag"); flash = root.Q("flash");
        header = root.Q("header"); instructions = root.Q("instructions");
        infoPanel = root.Q("infoPanel"); statList = root.Q("statList"); scanFill = root.Q("scanFill"); tagLayer = root.Q("tagLayer");
        infoCode = root.Q<Label>("infoCode"); infoTitle = root.Q<Label>("infoTitle"); infoSubtitle = root.Q<Label>("infoSubtitle");
        infoBody = root.Q<Label>("infoBody"); infoIndex = root.Q<Label>("infoIndex"); infoStatus = root.Q<Label>("infoStatus");
        returnButton = root.Q<Button>("returnButton"); backButton = root.Q<Button>("backButton");
        returnButton.clicked += () => ReturnClicked?.Invoke();
        backButton.clicked += () => BackClicked?.Invoke();

        var rh = root.Q("reticle"); reticle = new ReticleGraphic(); reticle.StretchToParentSize(); reticle.pickingMode = PickingMode.Ignore; rh.Add(reticle);
        var fh = root.Q("panelFrame"); var frame = new FrameGraphic(); frame.StretchToParentSize(); frame.pickingMode = PickingMode.Ignore; fh.Add(frame);
        var lh = root.Q("leaderLine"); leader = new LeaderGraphic(); leader.StretchToParentSize(); leader.pickingMode = PickingMode.Ignore; lh.Add(leader);
    }

    /// True when the pointer (screen pixels) is over an interactive HUD element (buttons, info panel).
    public bool PointerOverUI(Vector2 screen)
    {
        if (root?.panel == null) return false;
        var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
        for (var e = root.panel.Pick(p); e != null; e = e.parent)
            if (e is Button || items.Contains(e) || e == infoPanel) return infoPanel != e || infoShown;
        return false;
    }

    Vector2 ToPanel(Vector2 screen) => RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));

    // ---------------------------------------------------------------- forest
    public void SetReticle(bool visible, bool onTarget, Vector2 screen, string label = null)
    {
        forestLayer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible || root.panel == null) return;
        // the reticle sits at the aim point (screen centre when the mouse is captured, else the pointer)
        var p = ToPanel(screen);
        var r = root.Q("reticle"); r.style.left = p.x; r.style.top = p.y;
        reticleTag.style.left = p.x; reticleTag.style.top = p.y;
        reticle.target = Mathf.MoveTowards(reticle.target, onTarget ? 1 : 0, Time.unscaledDeltaTime * 5f);
        reticle.MarkDirtyRepaint();
        reticleTag.EnableInClassList("reticle-tag-visible", onTarget);
        if (onTarget && label != null) root.Q<Label>("reticleText").text = label;
    }

    public void SetFlash(float a) => flash.style.opacity = a;


    // ---------------------------------------------------------------- showcase
    public void ShowShowcase(AnimalFacts.Species sp)
    {
        var points = sp.points;
        root.Q<Label>("headerCode").text = sp.archive; root.Q<Label>("headerTitle").text = sp.title;
        root.Q<Label>("headerSpecies").text = sp.latin; root.Q<Label>("headerVariant").text = sp.variant;
        forestLayer.style.display = DisplayStyle.None;
        showcaseLayer.RemoveFromClassList("showcase-hidden");
        tagLayer.Clear(); tags.Clear();
        foreach (var pt in points)
        {
            var tag = new VisualElement(); tag.AddToClassList("tag"); tag.pickingMode = PickingMode.Ignore;
            var num = new Label(pt.code); num.AddToClassList("mono"); num.AddToClassList("tag-num"); num.pickingMode = PickingMode.Ignore;
            var lab = new Label(pt.title); lab.AddToClassList("mono"); lab.AddToClassList("tag-label"); lab.pickingMode = PickingMode.Ignore;
            tag.Add(num); tag.Add(lab); tagLayer.Add(tag); tags.Add(tag);
        }
        // clickable index of the points under the header
        var list = root.Q("pointList"); list.Clear(); items.Clear();
        for (int i = 0; i < points.Length; i++)
        {
            int idx = i;
            var it = new VisualElement(); it.AddToClassList("point-item");
            var n = new Label(points[i].code); n.AddToClassList("mono"); n.AddToClassList("point-num"); n.pickingMode = PickingMode.Ignore;
            var t = new Label(points[i].title); t.AddToClassList("mono"); t.AddToClassList("point-name"); t.pickingMode = PickingMode.Ignore;
            it.Add(n); it.Add(t);
            it.RegisterCallback<ClickEvent>(_ => PointClicked?.Invoke(idx));
            list.Add(it); items.Add(it);
        }
        // let the layout run once before starting the fade-in transitions
        root.schedule.Execute(() => { header.AddToClassList("header-in"); instructions.AddToClassList("instructions-in"); returnButton.AddToClassList("return-in"); }).StartingIn(50);
    }

    /// Place the hotspot tags; hidden ones are behind the camera or suppressed while a panel is open.
    public void UpdateTags(IList<Vector3> screen, int hover, int selected)
    {
        if (root.panel == null) return;
        for (int i = 0; i < tags.Count && i < screen.Count; i++)
        {
            var s = screen[i];
            // only the hovered point shows its tag (they overlap around the head); the selected one keeps it
            bool visible = s.z > 0 && (selected < 0 ? i == hover : i == selected);
            var p = ToPanel(new Vector2(s.x, s.y));
            tags[i].style.left = p.x + 16; tags[i].style.top = p.y - 11;
            tags[i].EnableInClassList("tag-hidden", !visible);
            tags[i].EnableInClassList("tag-hover", i == hover && selected < 0);
        }
        for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("point-selected", i == selected || (selected < 0 && i == hover));
    }

    public void ShowInfo(TigerFacts.Point pt, int index, int count)
    {
        infoCode.text = $"{pt.code}  //  ANALYSIS"; infoTitle.text = pt.title; infoSubtitle.text = pt.subtitle;
        infoIndex.text = $"{index + 1:00} / {count:00}"; infoStatus.text = "SCANNING";
        statList.Clear();
        var fills = new List<(VisualElement fill, float v)>();
        foreach (var st in pt.stats)
        {
            var row = new VisualElement(); row.AddToClassList("stat-row"); row.pickingMode = PickingMode.Ignore;
            var l = new Label(st.label.ToUpperInvariant()); l.AddToClassList("mono"); l.AddToClassList("stat-label");
            var right = new VisualElement(); right.AddToClassList("stat-right");
            var v = new Label(st.value); v.AddToClassList("mono"); v.AddToClassList("stat-value");
            right.Add(v);
            if (st.bar >= 0)
            {
                var bar = new VisualElement(); bar.AddToClassList("stat-bar");
                var fill = new VisualElement(); fill.AddToClassList("stat-bar-fill"); bar.Add(fill); right.Add(bar);
                fills.Add((fill, st.bar));
            }
            row.Add(l); row.Add(right); statList.Add(row);
        }
        bodyFull = pt.body; bodyStart = Time.unscaledTime + 0.35f; infoBody.text = "";
        scanStart = Time.unscaledTime;
        infoPanel.RemoveFromClassList("info-hidden");
        infoShown = true;
        root.schedule.Execute(() => { foreach (var (f, val) in fills) f.style.width = Length.Percent(val * 100); }).StartingIn(250);
    }

    public void HideInfo()
    {
        infoPanel.AddToClassList("info-hidden");
        infoShown = false; leader.visible01 = 0; leader.MarkDirtyRepaint();
    }

    /// Leader line from the selected point (screen pixels) to the panel's left edge.
    public void UpdateLeader(Vector3 screen)
    {
        if (root.panel == null) return;
        leader.visible01 = Mathf.MoveTowards(leader.visible01, infoShown && screen.z > 0 ? 1 : 0, Time.unscaledDeltaTime * 3f);
        leader.from = ToPanel(new Vector2(screen.x, screen.y));
        var wb = infoPanel.worldBound;
        leader.to = new Vector2(wb.xMin, wb.yMin + 54);
        leader.MarkDirtyRepaint();
    }

    void Update()
    {
        if (!infoShown) return;
        // typewriter body text and scan bar
        int n = Mathf.Clamp((int)((Time.unscaledTime - bodyStart) * 140f), 0, bodyFull.Length);
        if (infoBody.text.Length != n) infoBody.text = bodyFull.Substring(0, n);
        float s = Mathf.Clamp01((Time.unscaledTime - scanStart) / 0.9f);
        scanFill.style.width = Length.Percent(s * 100);
        if (s >= 1 && infoStatus.text != "SCAN COMPLETE") infoStatus.text = "SCAN COMPLETE";
    }

    // ---------------------------------------------------------------- Painter2D graphics
    class ReticleGraphic : VisualElement
    {
        public float target;
        public ReticleGraphic() { generateVisualContent += Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = contentRect; var c = r.center;
            float k = target, rad = Mathf.Lerp(5f, 20f, k);
            p.strokeColor = new Color(1, 1, 1, Mathf.Lerp(0.75f, 0f, k)); p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(c, 3f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.strokeColor = new Color(Accent.r, Accent.g, Accent.b, k); p.lineWidth = 2f;
            float L = 8f;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var corner = c + new Vector2(sx * rad, sy * rad);
                p.BeginPath(); p.MoveTo(corner - new Vector2(sx * L, 0)); p.LineTo(corner); p.LineTo(corner - new Vector2(0, sy * L)); p.Stroke();
            }
            if (k > 0.01f) { p.fillColor = new Color(Accent.r, Accent.g, Accent.b, k); p.BeginPath(); p.Arc(c, 2.5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        }
    }

    class FrameGraphic : VisualElement
    {
        public FrameGraphic() { generateVisualContent += Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = contentRect; float L = 18f;
            p.strokeColor = Accent; p.lineWidth = 2f;
            void Corner(Vector2 a, Vector2 dx, Vector2 dy) { p.BeginPath(); p.MoveTo(a + dx * L); p.LineTo(a); p.LineTo(a + dy * L); p.Stroke(); }
            Corner(new Vector2(r.xMin, r.yMin), Vector2.right, Vector2.up);
            Corner(new Vector2(r.xMax, r.yMin), Vector2.left, Vector2.up);
            Corner(new Vector2(r.xMax, r.yMax), Vector2.left, Vector2.down);
            Corner(new Vector2(r.xMin, r.yMax), Vector2.right, Vector2.down);
            p.strokeColor = new Color(Accent.r, Accent.g, Accent.b, 0.25f); p.lineWidth = 1f;
            p.BeginPath(); p.MoveTo(new Vector2(r.xMin + 6, r.yMin + 6)); p.LineTo(new Vector2(r.xMax - 6, r.yMin + 6)); p.LineTo(new Vector2(r.xMax - 6, r.yMax - 6));
            p.LineTo(new Vector2(r.xMin + 6, r.yMax - 6)); p.ClosePath(); p.Stroke();
        }
    }

    class LeaderGraphic : VisualElement
    {
        public Vector2 from, to; public float visible01;
        public LeaderGraphic() { generateVisualContent += Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            if (visible01 <= 0.01f) return;
            var p = ctx.painter2D;
            var elbow = new Vector2(Mathf.Lerp(from.x, to.x, 0.55f), to.y);
            var a = new Color(Accent.r, Accent.g, Accent.b, 0.9f * visible01);
            p.strokeColor = a; p.lineWidth = 1.5f;
            // draw progressively: from the point to the elbow, then along to the panel
            float k = visible01;
            var mid = Vector2.Lerp(from, elbow, Mathf.Clamp01(k * 2));
            p.BeginPath(); p.MoveTo(from); p.LineTo(mid);
            if (k > 0.5f) p.LineTo(Vector2.Lerp(elbow, to, (k - 0.5f) * 2));
            p.Stroke();
            p.fillColor = a; p.BeginPath(); p.Arc(from, 4f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.strokeColor = a; p.BeginPath(); p.Arc(from, 9f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
        }
    }
}
