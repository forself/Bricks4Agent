using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Runs a test action in the middle of a broker request: a decorated broker service (see
/// <see cref="HookedService{T}"/>) calls the armed action right before a matching call reaches the real
/// service. This places an event (a kill switch, a credential revocation) at an exact point inside a
/// request instead of hoping a race lands there. Every armed action runs at most once.
/// </summary>
public sealed class ServiceCallHooks
{
    private readonly object _gate = new();
    private readonly List<ArmedHook> _armed = new();

    /// <summary>
    /// Arms <paramref name="action"/> to run before the next call of <typeparamref name="TService"/>.<paramref name="method"/>
    /// whose arguments satisfy <paramref name="when"/>. Dispose the result to disarm a hook that did not fire.
    /// </summary>
    public ArmedHook Arm<TService>(string method, Func<object?[], bool> when, Action action)
    {
        var hook = new ArmedHook(this, $"{typeof(TService).Name}.{method}", when, action);
        lock (_gate)
        {
            _armed.Add(hook);
        }

        return hook;
    }

    internal void BeforeCall(string method, object?[] args)
    {
        ArmedHook? fired = null;
        lock (_gate)
        {
            foreach (var hook in _armed)
            {
                if (string.Equals(hook.Method, method, StringComparison.Ordinal) && hook.When(args))
                {
                    fired = hook;
                    break;
                }
            }

            if (fired is not null)
            {
                _armed.Remove(fired);
            }
        }

        if (fired is not null)
        {
            fired.Run();
        }
    }

    internal void Disarm(ArmedHook hook)
    {
        lock (_gate)
        {
            _armed.Remove(hook);
        }
    }

    /// <summary>Replaces the registration of <typeparamref name="TService"/> with a decorator that reports every call to <paramref name="hooks"/>.</summary>
    public static void Decorate<TService>(IServiceCollection services, ServiceCallHooks hooks)
        where TService : class
    {
        var descriptor = services.Last(candidate => candidate.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(TService),
            provider => HookedService<TService>.Wrap((TService)CreateInner(descriptor, provider), hooks),
            descriptor.Lifetime));
    }

    private static object CreateInner(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(provider);
        }

        return ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}

/// <summary>An armed hook; <see cref="Fired"/> tells whether it ran.</summary>
public sealed class ArmedHook : IDisposable
{
    private readonly ServiceCallHooks _owner;
    private readonly Action _action;
    private int _fired;

    internal ArmedHook(ServiceCallHooks owner, string method, Func<object?[], bool> when, Action action)
    {
        _owner = owner;
        Method = method;
        When = when;
        _action = action;
    }

    internal string Method { get; }

    internal Func<object?[], bool> When { get; }

    public bool Fired => Volatile.Read(ref _fired) == 1;

    internal void Run()
    {
        Interlocked.Exchange(ref _fired, 1);
        _action();
    }

    public void Dispose() => _owner.Disarm(this);
}

/// <summary>Interface decorator that reports each call to <see cref="ServiceCallHooks"/> before forwarding it.</summary>
public class HookedService<T> : DispatchProxy
    where T : class
{
    private T _inner = null!;
    private ServiceCallHooks _hooks = null!;

    public static T Wrap(T inner, ServiceCallHooks hooks)
    {
        var proxy = Create<T, HookedService<T>>();
        var decorator = (HookedService<T>)(object)proxy;
        decorator._inner = inner;
        decorator._hooks = hooks;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        _hooks.BeforeCall($"{typeof(T).Name}.{targetMethod.Name}", args ?? Array.Empty<object?>());
        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>
/// The authorization test host with <see cref="BrokerCore.Services.ISessionService"/> decorated, so a test can
/// run an action (a kill switch, a credential revocation) at an exact point inside a register or heartbeat request.
/// It has a broker and database of its own: tests that advance the epoch do not affect other test classes.
/// </summary>
public sealed class HookedBrokerFixture : BrokerAuthorizationFixture
{
    public HookedBrokerFixture()
        : this(new ServiceCallHooks())
    {
    }

    private HookedBrokerFixture(ServiceCallHooks hooks)
        : base(enforceWorkerAuth: true, configureTestServices: services =>
            ServiceCallHooks.Decorate<BrokerCore.Services.ISessionService>(services, hooks))
    {
        Hooks = hooks;
    }

    public ServiceCallHooks Hooks { get; }
}
