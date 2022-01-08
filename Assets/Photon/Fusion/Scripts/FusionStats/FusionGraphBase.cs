using System;
using UnityEngine;
using Fusion;
using UI = UnityEngine.UI;
using Stats = Fusion.Simulation.Statistics;
using Fusion.StatsInternal;

public abstract class FusionGraphBase : Fusion.Behaviour, IFusionStatsView {

  protected const int PAD = FusionStatsUtilities.PAD;
  protected const int MRGN = FusionStatsUtilities.MARGIN;
  protected const int MAX_FONT_SIZE_WITH_GRAPH = 24;

  protected Stats.StatSourceInfo _dataSourceInfo;

  [SerializeField] [HideInInspector] public int Places = 0;
  //[SerializeField] [HideInInspector] public double Multiplier = 1;
  //[SerializeField] [HideInInspector] public Stats.StatsPer PerFlags;
  //[SerializeField] [HideInInspector] public Stats.StatsPer DefaultPer;
  [SerializeField] [HideInInspector] protected UI.Text  LabelTitle;
  [SerializeField] [HideInInspector] protected UI.Image BackImage;

  [SerializeField]
  protected Stats.StatSourceTypes _statSourceType;
  public Stats.StatSourceTypes StateSourceType {
    get => _statSourceType;
    set {
      _statSourceType = value;
      TryConnect();
    }
  }

  [SerializeField]
  [CastEnum(nameof(CastToStatType))]
  protected int _statId;
  public int StatId {
    get => _statId;
    set {
      _statId = value;
      TryConnect();
    }
  }

  protected IStatsBuffer _statsBuffer;
  public IStatsBuffer StatsBuffer {
    get {
      if (_statsBuffer == null) {
        TryConnect();
      }
      return _statsBuffer;
    }
  }

  protected bool _isOverlay;
  public bool IsOverlay {
    set {
      if (_isOverlay != value) {
        _isOverlay = value;
        CalculateLayout();
        _layoutDirty = true;
      }
    }
    get {
      return _isOverlay;
    }
  }

  protected virtual Color BackColor {
    get {
      if (_statSourceType == Stats.StatSourceTypes.Simulation) {
        return _fusionStats.SimDataBackColor;
      }
      if (_statSourceType == Stats.StatSourceTypes.NetConnection) {
        return _fusionStats.NetDataBackColor;
      }
      return _fusionStats.ObjDataBackColor;
    }
  }

  protected Type CastToStatType => (_statSourceType == Stats.StatSourceTypes.Simulation) ? typeof(Stats.SimStats) : typeof(Stats.ObjStats);

  protected IFusionStats _fusionStats;
  protected bool _layoutDirty = true;

  protected Stats.StatsPer CurrentPer;

  public virtual void Initialize() {

  }


  public void CyclePer() {
    switch (_dataSourceInfo.PerFlags) {
      case Stats.StatsPer.Individual:
        CurrentPer = Stats.StatsPer.Individual;
        return;

      case Stats.StatsPer.Tick:
        CurrentPer = CurrentPer == Stats.StatsPer.Individual ? Stats.StatsPer.Tick : Stats.StatsPer.Individual;
        return;

      case Stats.StatsPer.Second:
        CurrentPer = CurrentPer == Stats.StatsPer.Individual ? Stats.StatsPer.Second : Stats.StatsPer.Individual;
        return;

      default: // Both Ticks and Secs
        CurrentPer = 
          CurrentPer == Stats.StatsPer.Individual ? Stats.StatsPer.Tick :
          CurrentPer == Stats.StatsPer.Tick ? Stats.StatsPer.Second :
          Stats.StatsPer.Individual;
        return;
    }
  }

  public abstract void CalculateLayout();

  public abstract void Refresh();

  protected virtual bool TryConnect() {

    if (gameObject.activeInHierarchy == false) {
      return false;
    }

    if (_fusionStats == null) {
      _fusionStats = GetComponentInParent<IFusionStats>();
    }

    // Any data connection requires a runner for the statistics source.
    var runner = _fusionStats?.Runner;

    //NetworkRunner runner;
    //if (_fusionStats != null) {
    //  runner = _fusionStats.Runner;
    //} 
    //else {
    //  if (FusionStatsUtilities.TryFindActiveRunner(gameObject, out var foundrunner)) {
    //    runner = foundrunner;
    //  } else {
    //    runner = null;
    //  }
    //}

    // We still run through this even if the runner/stats are not ready - to get the name/multi-peer values.


    var statistics = runner?.Simulation?.Stats;
    Stats.StatSourceInfo info;

    info = Stats.GetDescription(_statSourceType, _statId);

    switch (_statSourceType) {
      case Stats.StatSourceTypes.Simulation: {
          _statsBuffer = statistics?.GetStatBuffer((Stats.SimStats)_statId);
          break;
        }
      case Stats.StatSourceTypes.NetworkObject: {
          if (_statId >= Stats.OBJ_STAT_TYPE_COUNT) {
            StatId = 0;
          }
          if (_fusionStats.Object == null) {
            _statsBuffer = null;
            break;
          }

          _statsBuffer = statistics?.GetObjectBuffer(_fusionStats.Object.Id, (Stats.ObjStats)_statId, true);
          break;
        }
      case Stats.StatSourceTypes.NetConnection: {

          info = Stats.GetDescription((Stats.NetStats)_statId);
          if (runner == null) {
            _statsBuffer = null;
            break;
          }
          _statsBuffer = statistics?.GetStatBuffer((Stats.NetStats)_statId, runner);
          break;
        }
      default: {
          _statsBuffer = null;
          break;
        }
    }
    if (BackImage) {
      BackImage.color = BackColor;
    }

    // Update the labels, regardless if a connection can be made.
    if (LabelTitle) {
      LabelTitle.text = info.LongName;
    }

    _dataSourceInfo = info;

    //Multiplier = info.Multiplier;
    //PerFlags = info.per;
    CurrentPer = info.PerDefault;
      //((info.per & Stats.StatsPer.Tick)   != 0) ? Stats.StatsPer.Tick :
      //((info.per & Stats.StatsPer.Second) != 0) ? Stats.StatsPer.Second : 
      //Stats.StatsPer.Individual;

    return (_statsBuffer != null);
  }
}
