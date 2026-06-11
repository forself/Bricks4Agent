namespace Broker.Services;

/// <summary>
/// broker 在叢集中的角色。Single = 無叢集(單機、永遠主);Primary/Standby = 有 etcd 選主時。
/// </summary>
public enum NodeRole { Starting, Single, Primary, Standby }

/// <summary>
/// 叢集選主抽象。階段①(唯讀):只暴露「我是不是主」,不 gate 任何行為。
/// 之後階段②/④的 LeaderGuard 會查 IsPrimary 來 gate 真錢 dispatch(自我 fence)。
/// </summary>
public interface ILeaderElection
{
    bool IsPrimary { get; }
    NodeRole Role { get; }
    string NodeId { get; }
}

/// <summary>
/// 單機預設實作:永遠 PRIMARY。無 etcd 設定時用這個 → 行為與現行單機 broker 完全一致(零風險)。
/// </summary>
public sealed class SingleNodeLeaderElection : ILeaderElection
{
    public bool IsPrimary => true;
    public NodeRole Role => NodeRole.Single;
    public string NodeId => "single";
}
