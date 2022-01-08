using System;
using UnityEngine;
using Fusion;
using UI = UnityEngine.UI;
using Stats = Fusion.Simulation.Statistics;
using Fusion.StatsInternal;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FusionGraph : FusionGraphBase {

  public enum Layouts {
    Auto,
    Full,
    CenteredBasic,
    Compact
  }

  public enum ShowGraphOptions {
    Never,
    OverlayOnly,
    Always,
  }

  const int GRPH_TOP_PAD = 36;
  const int GRPH_BTM_PAD = 36;
  const int HIDE_XTRAS_WDTH = 200;
  const int INTERMITTENT_DATA_ARRAYSIZE = 128;

  const int EXPAND_GRPH_THRESH = GRPH_BTM_PAD + GRPH_TOP_PAD + 40;
  const int COMPACT_THRESH = GRPH_BTM_PAD + GRPH_TOP_PAD - 20;

  const float LABEL_HEIGHT = 0.25f;

  static Shader Shader {
    get => Resources.Load<Shader>("FusionGraphShader");
  }

  public float Padding = 5f;

  [SerializeField] public UI.Image GraphImg;
  [SerializeField] public UI.Text LabelMin;
  [SerializeField] public UI.Text LabelMax;
  [SerializeField] public UI.Text LabelAvg;
  [SerializeField] public UI.Text LabelLast;

  [SerializeField] [HideInInspector] UI.Dropdown _viewDropdown;
  [SerializeField] [HideInInspector] UI.Button   _avgBttn;

  [SerializeField] public float  Height = 50;

  [SerializeField] [HideInInspector] RectTransform _rt;
  [SerializeField] [HideInInspector] RectTransform _titleRT;
  [SerializeField] [HideInInspector] RectTransform _graphRT;
  [SerializeField] [HideInInspector] RectTransform _avgRT;
  [SerializeField] [HideInInspector] RectTransform _minRT;
  [SerializeField] [HideInInspector] RectTransform _maxRT;
  [SerializeField] [HideInInspector] RectTransform _lstRT;

  [SerializeField]
  Layouts _layout;
  public Layouts Layout {
    get => _layout;
    set {
      _layout = value;
      CalculateLayout();
    }
  }

  [SerializeField]
  ShowGraphOptions _showGraph = ShowGraphOptions.OverlayOnly;
  public ShowGraphOptions ShowGraph {
    get => _showGraph;
    set {
      _showGraph = value;
      CalculateLayout();
      _layoutDirty = true;
    }
  }

  [SerializeField]
  bool _alwaysExpandGraph;
  public bool AlwaysExpandGraph {
    get => _alwaysExpandGraph;
    set {
      _alwaysExpandGraph = value;
      CalculateLayout();
      _layoutDirty = true;
    }
  }

  float _min;
  float _max;
  float[] _values;
  float[] _intensity;
  float[] _histogram;

#if UNITY_EDITOR
  private void OnValidate() {
    if (Application.isPlaying == false) {
      //This is here so when changes are made that affect graph names/colors they get applied immediately.
      TryConnect();
    }

#if UNITY_EDITOR
    if (Selection.activeGameObject == gameObject) {
      UnityEditor.EditorApplication.delayCall += CalculateLayout;
    }
#endif
    _layoutDirty = true;
  }
#endif

  List<int> DropdownLookup = new List<int>();

  protected override bool TryConnect() {
    if (base.TryConnect()) {

      var flags = _statsBuffer.VisualizationFlags;

      DropdownLookup.Clear();
      _viewDropdown.ClearOptions();
      for (int i = 0; i < 16; ++i) {
        if (((int)flags & (1 << i)) != 0) {
          DropdownLookup.Add(1 << i);
          _viewDropdown.options.Add(new UI.Dropdown.OptionData(FusionStatsUtilities.CachedTelemetryNames[i + 1]));
          if ((1 << i & (int)_statsBuffer.DefaultVisualization) != 0) {
            _viewDropdown.value = i - 1;
          }
        }
      }

      return true;
    } else return false;
  }

  [InlineHelp]
  FusionGraphVisualization _graphVisualization;
  public FusionGraphVisualization GraphVisualization {
    set {
      _graphVisualization = value;
      Reset();
    }
  }

  private void Reset() {
    _values = null;
    _histogram = null;
    _intensity = null;
    _min = 0;
    _max = 0;

    ResetGraphShader();
  }

  private void Awake() {
    _rt = GetComponent<RectTransform>();
  }

  public void Clear() {
    if (_values != null && _values.Length > 0) {
      Array.Clear(_values, 0, _values.Length);
      Array.Clear(_histogram, 0, _histogram.Length);
      Array.Clear(_intensity, 0, _intensity.Length);
      _min = 0;
      _max = 0;
      _histoHighestUsedBucketIndex = 0;
      _histoHighestValue = 0;
      _histoAvg = 0;
      _histoAvgSampleCount = 0;
    }
  }

  public override void Initialize() {

    _viewDropdown?.onValueChanged.AddListener(OnDropdownChanged);
    _avgBttn?.onClick.AddListener(CyclePer);
  }

  public void OnDropdownChanged(int value) {
    GraphVisualization = (FusionGraphVisualization)DropdownLookup[value];
  }

  void ResetGraphShader() {
    if (GraphImg) {
      GraphImg.material = new Material(Shader);
      GraphImg.material.SetColor("_GoodColor", _fusionStats.GraphColorGood);
      GraphImg.material.SetColor("_BadColor", _fusionStats.GraphColorBad);
      GraphImg.material.SetInt("EndCap", 0);
      GraphImg.material.SetInt("AnimateInOut", 0);
    }
  }

  /// <summary>
  /// Returns true if the graph rendered. False if the size was too small and the graph was hidden.
  /// </summary>
  /// <returns></returns>
  public override void Refresh() {

    if (_layoutDirty) {
      CalculateLayout();
    }

    var statsBuffer = StatsBuffer;
    if (statsBuffer == null || statsBuffer.Count < 1) {
      return;
    }
    
    var visualization = _graphVisualization == FusionGraphVisualization.Auto ? _statsBuffer.DefaultVisualization : _graphVisualization;

    if (_values == null) {
      int size =
        visualization == FusionGraphVisualization.ContinuousTick ? statsBuffer.Capacity :
        visualization == FusionGraphVisualization.ValueHistogram ? _histoBucketCount + 3 :
        INTERMITTENT_DATA_ARRAYSIZE;

      _values    = new float[size];
      _histogram = new float[size];
      _intensity = new float[size];
    }

    if (_cachedValues == null) {
      _cachedValues = new (int, float)[INTERMITTENT_DATA_ARRAYSIZE];
    }

    switch (visualization) {
      case FusionGraphVisualization.ContinuousTick: {
          UpdateContinuousTick(statsBuffer);
          break;
        }
      case FusionGraphVisualization.IntermittentTick: {
          UpdateIntermittentTick(statsBuffer);
          break;
        }
      case FusionGraphVisualization.IntermittentTime: {
          UpdateIntermittentTime(statsBuffer);
          break;
        }
      case FusionGraphVisualization.CountHistogram: {
          //UpdateIntermittentTime(data);
          break;
        }
      case FusionGraphVisualization.ValueHistogram: {
          UpdateTickValueHistogram(statsBuffer);
          break;
        }
    }
  }

  void UpdateContinuousTick(IStatsBuffer data) 
    {
    var min = float.MaxValue;
    var max = float.MinValue;
    var avg = 0f;
    var last = 0f;

    for (int i = 0; i < data.Count; ++i) {
      var v = (float)(_dataSourceInfo.Multiplier * data.GetSampleAtIndex(i).FloatValue);

      min = Math.Min(v, min);
      max = Math.Max(v, max);

      if (i >= _values.Length)
        Debug.LogWarning(name + " Out of range " + i + " " + _values.Length + " " + data.Count);
      _values[i] = last = v;

      avg += v;
    }

    avg /= data.Count;

    if (min > 0) {
      min = 0;
    }

    if (max > _max) {
      _max = max;
    }

    if (min < _min) {
      _min = min;
    }

    {
      var r = _max - _min;

      for (int i = 0; i < data.Count; ++i) {
        _values[i] = Mathf.Clamp01((_values[i] - _min) / r);
      }
    }

    if (LabelMin)  { LabelMin.text  = Math.Round(min,  Places).ToString(); }
    if (LabelMax)  { LabelMax.text  = Math.Round(max,  Places).ToString(); }
    if (LabelAvg)  { LabelAvg.text  = Math.Round(avg,  Places).ToString(); }
    if (LabelLast) { LabelLast.text = Math.Round(last, Places).ToString(); }

    if (GraphImg && GraphImg.enabled) {
      GraphImg.material.SetFloatArray("_Data", _values);
      GraphImg.material.SetFloat("_Count", _values.Length);
      GraphImg.material.SetFloat("_Height", Height);
    }

    _min = Mathf.Lerp(_min, 0, Time.deltaTime);
    _max = Mathf.Lerp(_max, 1, Time.deltaTime);
  }

  (int tick, float value)[] _cachedValues;
  int _lastCachedTick;

  void UpdateIntermittentTick(IStatsBuffer data) {

    int latestServerStateTick = _fusionStats.Runner.Simulation.LatestServerState.Tick;

    var min = float.MaxValue;
    var max = float.MinValue;
    var sum = 0f;
    var last = 0f;

    //var oldestBufferedTick = data.GetSampleAtIndex(0).TickValue;
    var oldestAllowedBufferedTick = latestServerStateTick - INTERMITTENT_DATA_ARRAYSIZE + 1;

    var tailIndex = latestServerStateTick % INTERMITTENT_DATA_ARRAYSIZE;
    var headIndex = (tailIndex + 1) % INTERMITTENT_DATA_ARRAYSIZE;

    int gapcheck = _lastCachedTick;
    // Copy all data from the buffer into our larger intermediate cached buffer
    for (int i = 0; i < data.Count; ++i) {
      var sample = data.GetSampleAtIndex(i);
      var sampleTick = sample.TickValue;
      
      // sample on buffer is older than the range we are displaying.
      if (sampleTick < oldestAllowedBufferedTick) {
        gapcheck = sampleTick;
        continue;
      }

      // sample on the buffer has already been merged into cached buffer.
      if (sampleTick <= _lastCachedTick) {
        gapcheck = sampleTick;
        continue;
      }

      // Fill any gaps in the buffer data 
      var gap = sampleTick - gapcheck;
      for (int g = gapcheck + 1; g < sampleTick; ++g) {
        _cachedValues[g % INTERMITTENT_DATA_ARRAYSIZE] = (g, 0);
      }

      _lastCachedTick = sampleTick;
      _cachedValues[sampleTick % INTERMITTENT_DATA_ARRAYSIZE] = (sampleTick, (float)(_dataSourceInfo.Multiplier * sample.FloatValue));

      gapcheck = sampleTick;
    }

    // Loop through once to determine scaling
    for (int i = 0; i < INTERMITTENT_DATA_ARRAYSIZE; ++i) {
      var sample = _cachedValues[(i + headIndex) % INTERMITTENT_DATA_ARRAYSIZE];
      var v = sample.value;
      // Any outdated values are ticks that had no data, set them to zero.
      if (sample.tick < oldestAllowedBufferedTick) {
        sample.tick = oldestAllowedBufferedTick + i;
        sample.value = v = 0;
      }

      min = Math.Min(v, min);
      max = Math.Max(v, max);

      _values[i] = last = v;

      sum += v;
    }

    //sum /= INTERMITTENT_DATA_ARRAYSIZE;

    if (min > 0) {
      min = 0;
    }

    if (max > _max) {
      _max = max;
    }

    if (min < _min) {
      _min = min;
    }

    {
      var r = _max - _min;

      // Loop again to apply scaling
      for (int i = 0; i < INTERMITTENT_DATA_ARRAYSIZE; ++i) {
        _values[i] = Mathf.Clamp01((_values[i] - _min) / r);
      }
    }

    if (LabelMin) { LabelMin.text = Math.Round(min, Places).ToString(); }
    if (LabelMax) { LabelMax.text = Math.Round(max, Places).ToString(); }
    if (LabelLast) { LabelLast.text = Math.Round(last, Places).ToString(); }

    if (LabelAvg) {
      GetAverageInfo(data, sum);
    }

    if (GraphImg && GraphImg.enabled) {
      GraphImg.material.SetFloatArray("_Data", _values);
      GraphImg.material.SetFloat("_Count", _values.Length);
      GraphImg.material.SetFloat("_Height", Height);
    }

    _min = Mathf.Lerp(_min, 0, Time.deltaTime);
    _max = Mathf.Lerp(_max, 1, Time.deltaTime);
  }

  void UpdateIntermittentTime(IStatsBuffer data) {
    var min = float.MaxValue;
    var max = float.MinValue;
    var sum = 0f;
    var last = 0f;

    for (int i = 0; i < data.Count; ++i) {
      var v = (float)(_dataSourceInfo.Multiplier * data.GetSampleAtIndex(i).FloatValue);

      min = Math.Min(v, min);
      max = Math.Max(v, max);

      _values[i] = last = v;

      sum += v;
    }

    //switch (CurrentPer) {
    //  case Stats.StatsPer.Second: {
    //      var oldestTimeRecord = data.GetSampleAtIndex(0).TimeValue;
    //      var currentTime = (float)_fusionStats.Runner.Simulation.LatestServerState.Time;
    //      avg /= currentTime - oldestTimeRecord;
    //      break;
    //    }

    //  case Stats.StatsPer.Tick:
    //    {
    //      var oldestTickRecord = data.GetSampleAtIndex(0).TickValue;
    //      var currentTick = (float)_fusionStats.Runner.Simulation.LatestServerState.Tick;
    //      avg /= currentTick - oldestTickRecord;
    //      break;
    //    }

    //  default: {
    //      avg /= data.Count;
    //      break;
    //    }

    //}


    //avg /= currentTime - oldestTimeRecord;

    if (min > 0) {
      min = 0;
    }

    if (max > _max) {
      _max = max;
    }

    if (min < _min) {
      _min = min;
    }

    {
      var r = _max - _min;

      for (int i = 0; i < data.Count; ++i) {
        _values[i] = Mathf.Clamp01((_values[i] - _min) / r);
      }
    }

    if (LabelMin) { LabelMin.text = Math.Round(min, Places).ToString(); }
    if (LabelMax) { LabelMax.text = Math.Round(max, Places).ToString(); }
    if (LabelLast) { LabelLast.text = Math.Round(last, Places).ToString(); }

    if (LabelAvg) {
      GetAverageInfo(data, sum);
    }
    //if (LabelAvg) { 
    //  if (CurrentPer == Stats.StatsPer.Individual) {
    //    LabelAvg.text = Math.Round(avg, Fractions).ToString();
    //  } else if (CurrentPer == Stats.StatsPer.Second) {
    //    LabelAvg.text = Math.Round(avg, Fractions).ToString() + "/s";
    //  } else if (CurrentPer == Stats.StatsPer.Tick) {
    //  LabelAvg.text = Math.Round(avg, Fractions).ToString() + "/t";
    //  }
    //}

    if (GraphImg && GraphImg.enabled) {
      GraphImg.material.SetFloatArray("_Data", _values);
      GraphImg.material.SetFloat("_Count", _values.Length);
      GraphImg.material.SetFloat("_Height", Height);
    }

    _min = Mathf.Lerp(_min, 0, Time.deltaTime);
    _max = Mathf.Lerp(_max, 1, Time.deltaTime);
  }

  void GetAverageInfo(IStatsBuffer data, float sum) {
    switch (CurrentPer) {
      case Stats.StatsPer.Second: {
          var oldestTimeRecord = data.GetSampleAtIndex(0).TimeValue;
          var currentTime = (float)_fusionStats.Runner.Simulation.LatestServerState.Time;
          var avg = sum / (currentTime - oldestTimeRecord);
          LabelAvg.text = Math.Round(avg, Places).ToString() + "\u00a0/sec";
          break;
        }

      case Stats.StatsPer.Tick: {
          var oldestTickRecord = data.GetSampleAtIndex(0).TickValue;
          var currentTick = (float)_fusionStats.Runner.Simulation.LatestServerState.Tick;
          var avg = sum / (currentTick - oldestTickRecord);
          LabelAvg.text = Math.Round(avg, Places).ToString() + "\u00a0/tick";
          break;
        }

      default: {
          var avg = sum / _values.Length; // data.Count;
          LabelAvg.text = Math.Round(avg, Places).ToString();
          break;
        }
    }
  }

  int    _histoBucketCount    = 1024 - 4;
  int    _histoBucketMaxValue = 1020;
  int    _histoHighestUsedBucketIndex;
  int    _histoAvgSampleCount;
  double _histoStep;
  double _histoHighestValue;
  double _histoAvg;

  void UpdateTickValueHistogram(IStatsBuffer data) {

    // Determine histogram bucketsizes if they haven't yet been determined.
    if (_histoStep == 0) {
      _histoStep = (double)_histoBucketCount / _histoBucketMaxValue;
    }

    int latestServerStateTick = _fusionStats.Runner.Simulation.LatestServerState.Tick;
    int mostCurrentBufferTick = data.GetSampleAtIndex(data.Count - 1).TickValue;

    // count non-existent ticks as zero values
    if (mostCurrentBufferTick < latestServerStateTick) {
      int countbackto = Math.Max(mostCurrentBufferTick, _lastCachedTick);
      int newZeroCount = latestServerStateTick - countbackto;
      float zerocountTotal = _histogram[0] + newZeroCount;
      _histogram[0] = zerocountTotal;

      if (zerocountTotal > _max) {
        _max = zerocountTotal;
      }
    }

    double multiplier = _dataSourceInfo.Multiplier;
    // Read data in stat buffer backwards until we reach a tick already recorded
    for (int i = data.Count - 1; i >= 0; --i) {
      var v = (float)(multiplier * data.GetSampleAtIndex(i).FloatValue);

      var sample = data.GetSampleAtIndex(i);
      var tick = sample.TickValue;

      if (tick <= _lastCachedTick) {
        break;
      }

      var val = sample.FloatValue * multiplier;

      int bucketIndex;
      if (val == 0) {
        bucketIndex = 0;
      }
      else if (val == _histoBucketMaxValue) {
        bucketIndex = _histoBucketCount;

      } 
      else if (val > _histoBucketMaxValue) {
        bucketIndex = _histoBucketCount + 1;
      }      
      else {
        bucketIndex = (int)(val * _histoStep) + 1;
      }

      _histoAvg = (_histoAvg * _histoAvgSampleCount + val) / (++_histoAvgSampleCount);

      var newval = _histogram[bucketIndex] + 1;
      //Debug.LogWarning(name + " Value Histo Refresh " + data.Count + " i:" + i +" tick:" + tick + " val:" + val + " step:" + _step + " idx:"  +  bucketIndex + " bcount:" + _bucketCount + " newval:" + newval);
      
      if (newval > _max) {
        _max = newval;
      }
      _histogram[bucketIndex] = newval;


      if (val > _histoHighestValue) {
        _histoHighestValue = val;
        _histoHighestUsedBucketIndex = bucketIndex;
      }
    }

    int medianIndex = 0;
    float mostValues = 0;
    {
      var r = (_max - _min) * 1.1f;

      // Loop again to apply scaling
      for (int i = 0, cnt = _histogram.Length; i < cnt; ++i) {
        var value = _histogram[i];
        _intensity[i] = 0;
        if (i != 0 && value > mostValues) {
          mostValues = value;
          medianIndex = i;
        }
        _values[i] = Mathf.Clamp01((_histogram[i] - _min) / r);
      }
    }

    _intensity[medianIndex] = 1f;

    _lastCachedTick = latestServerStateTick;

    if (GraphImg && GraphImg.enabled) {
      GraphImg.material.SetFloatArray("_Data", _values);
      GraphImg.material.SetFloatArray("_Intensity", _intensity);
      GraphImg.material.SetFloat("_Count", _histoHighestUsedBucketIndex);
      GraphImg.material.SetFloat("_Height", Height);
    }

    _min = 0;

    LabelMax.text = $"<color=yellow>{Math.Ceiling((medianIndex + 1) * _histoStep)}</color>";
    LabelAvg.text = $"{Math.Ceiling(_histoAvg)}  per tick";
    LabelMin.text = Math.Round(_min, Places).ToString();
    LabelLast.text = (Math.Ceiling(_histoHighestValue - 2)).ToString();

  }

  public static FusionGraph Create(IFusionStats iFusionStats, Stats.StatSourceTypes statSourceType, int statId, RectTransform parentRT) {
    
    var statInfo = Stats.GetDescription(statSourceType, statId);

    //// If no explicit multiplier was given, use the default for this stat.
    //if (multiplier.HasValue == false) {
    //  multiplier = statInfo.Multiplier;
    //}

    var rootRT = parentRT.CreateRectTransform(statInfo.LongName);
    var graph = rootRT.gameObject.AddComponent<FusionGraph>();
    graph._fusionStats = iFusionStats;
    graph.Generate(statSourceType, (int)statId, rootRT);

    return graph;
  }

  public void Generate(Stats.StatSourceTypes type, int statId, RectTransform root) {

    _statSourceType = type;

    if (_rt == null) {
      _rt = GetComponent<RectTransform>();
    }

    //Places = fractions;
    //Multiplier = multiplier;
    _statId = statId;

    root.anchorMin = new Vector2(0.5f, 0.5f);
    root.anchorMax = new Vector2(0.5f, 0.5f);
    root.anchoredPosition3D = default;

    var background = root.CreateRectTransform("Background")
      .ExpandAnchor();

    BackImage = background.gameObject.AddComponent<UI.Image>();
    BackImage.color = BackColor;

    _graphRT = root.CreateRectTransform("Graph")
      .SetAnchors(0.0f, 1.0f, 0.2f, 0.8f)
      .SetOffsets(0.0f, 0.0f, 0.0f, 0.0f);

    GraphImg = _graphRT.gameObject.AddComponent<UI.Image>();
    ResetGraphShader();

    var fontColor = _fusionStats.FontColor;

    _titleRT = root.CreateRectTransform("Title")
      .ExpandAnchor()
      .SetOffsets(PAD, -PAD, 0.0f, -2.0f);
    _titleRT.anchoredPosition = new Vector2(0, 0);

    LabelTitle = _titleRT.AddText(name, TextAnchor.UpperRight, fontColor);
    LabelTitle.resizeTextMaxSize = MAX_FONT_SIZE_WITH_GRAPH;

    // Top Left value
    _maxRT = root.CreateRectTransform("Max")
      .SetAnchors(0.0f, 0.3f, 0.85f, 1.0f)
      .SetOffsets(MRGN, 0.0f, 0.0f, -2.0f);
    LabelMax = _maxRT.AddText("-", TextAnchor.UpperLeft, fontColor);

    // Bottom Left value
    _minRT = root.CreateRectTransform("Min")
      .SetAnchors(0.0f, 0.3f, 0.0f,  0.15f)
      .SetOffsets(MRGN, 0.0f, 0.0f, -2.0f);
    LabelMin = _minRT.AddText("-", TextAnchor.LowerLeft, fontColor);

    // Main Center value
    _avgRT = root.CreateRectTransform("Avg")
      .SetOffsets(0.0f, 0.0f, 0.0f, 0.0f)
      .SetOffsets(PAD, -PAD,  0.0f, 0.0f);
    _avgRT.anchoredPosition = new Vector2(0, 0);
    LabelAvg = _avgRT.AddText("-", TextAnchor.LowerCenter, fontColor);
    _avgBttn =  _avgRT.gameObject.AddComponent<UI.Button>();

    // Bottom Right value
    _lstRT = root.CreateRectTransform("Last")
      .SetAnchors(0.7f, 1.0f, 0.0f,  0.15f)
      .SetOffsets(PAD, -PAD,  0.0f, -2.0f);
    LabelLast = _lstRT.AddText("-", TextAnchor.LowerRight, fontColor);

    _viewDropdown = _titleRT.CreateDropdown(PAD, fontColor);

    _layoutDirty = true;
#if UNITY_EDITOR
    EditorUtility.SetDirty(this);
#endif

  }

  [BehaviourButtonAction("Update Layout")]
  public override void CalculateLayout() {
    // This Try/Catch is here to prevent errors resulting from a delayCall to this method when entering play mode.
    try {
      if (gameObject == null) {
        return;
      }
    } catch {
      return;
    }

    if (gameObject.activeInHierarchy == false) {
      return;
    }

    _layoutDirty = false;

    if (_rt == null) {
      _rt = GetComponent<RectTransform>();
    }

    if (_statsBuffer == null) {
      TryConnect();
    }

    bool isOverlayCanvas = _fusionStats != null && _fusionStats.CanvasType == FusionStats.StatCanvasTypes.Overlay;

    bool showGraph = ShowGraph == ShowGraphOptions.Always || (ShowGraph == ShowGraphOptions.OverlayOnly && isOverlayCanvas);

    var height = _rt.rect.height;
    var width  = _rt.rect.width;
    bool expandGraph = _alwaysExpandGraph || !showGraph || (_layout != Layouts.Full && height < EXPAND_GRPH_THRESH);
    bool isSuperShort = height < MRGN * 3;

    if (_graphRT) {
      _graphRT.gameObject.SetActive(showGraph);
      
      if (expandGraph) {
        _graphRT.SetAnchors(0, 1, 0, 1);
      } else {
        _graphRT.SetAnchors(0, 1, .2f, .8f);
      }
    }

    Layouts layout;
    if (_layout != Layouts.Auto) {
      layout = _layout;
    } else {
      if (height < COMPACT_THRESH) {
        layout = Layouts.Compact;
      } else {
        if (width < HIDE_XTRAS_WDTH) {
          layout = Layouts.CenteredBasic;
        } else {
          layout = Layouts.Full;
        }
      }
    }

    bool showExtras = layout == Layouts.Full /*|| (layout == Layouts.Compact && width > HIDE_XTRAS_WDTH)*/;

    switch (layout) {
      case Layouts.Full: {
          _titleRT.anchorMin = new Vector2(showExtras ? 0.3f : 0.0f, expandGraph ? 0.5f : 0.8f);
          _titleRT.anchorMax = new Vector2(1.0f, 1.0f);
          _titleRT.offsetMin = new Vector2(MRGN, MRGN);
          _titleRT.offsetMax = new Vector2(-MRGN, -MRGN);
          LabelTitle.alignment = showExtras ? TextAnchor.UpperRight : TextAnchor.UpperCenter;

          _avgRT.anchorMin = new Vector2(showExtras ? 0.3f : 0.0f, 0.0f);
          _avgRT.anchorMax = new Vector2(showExtras ? 0.7f : 1.0f, expandGraph ? 0.5f : 0.2f);
          LabelAvg.alignment = TextAnchor.LowerCenter;
          break;
        }
      case Layouts.CenteredBasic: {
          _titleRT.anchorMin = new Vector2(showExtras ? 0.3f : 0.0f, 0.5f);
          _titleRT.anchorMax = new Vector2(showExtras ? 0.7f : 1.0f, 1.0f);
          _titleRT.offsetMin = new Vector2(showExtras ? 0.0f : MRGN, MRGN);
          _titleRT.offsetMax = new Vector2(showExtras ? 0.0f : -MRGN, -MRGN);
          LabelTitle.alignment = TextAnchor.UpperCenter;

          _avgRT.anchorMin = new Vector2(0.0f, 0.0f);
          _avgRT.anchorMax = new Vector2(1.0f, expandGraph ? 0.5f : 0.2f);
          LabelAvg.alignment = TextAnchor.LowerCenter;
          break;
        }
      case Layouts.Compact: {
          _titleRT.anchorMin = new Vector2(0.0f, 0.0f);
          _titleRT.anchorMax = new Vector2(0.5f, 1.0f);
          if (isSuperShort) {
            _titleRT.SetOffsets(MRGN,     0, 0, 0);
            _avgRT  .SetOffsets(MRGN, -MRGN, 0, 0);
          } else {
            _titleRT.SetOffsets(MRGN, 0,  MRGN, -MRGN);
            _avgRT  .SetOffsets(0, -MRGN, MRGN, -MRGN);
          }
          LabelTitle.alignment = TextAnchor.MiddleLeft;

          _avgRT.anchorMin = new Vector2(0.5f, 0.0f);
          _avgRT.anchorMax = new Vector2(1.0f, 1.0f);
          LabelAvg.alignment = TextAnchor.MiddleRight;
          break;
        }
    }

    LabelMin.enabled = showExtras;
    LabelMax.enabled = showExtras;
    LabelLast.enabled = showExtras;
  }

}