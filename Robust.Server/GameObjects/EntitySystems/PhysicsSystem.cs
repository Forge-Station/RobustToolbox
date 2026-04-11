using JetBrains.Annotations;
using Prometheus;
using Robust.Server.Physics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Robust.Server.GameObjects
{
    [UsedImplicitly]
    public sealed class PhysicsSystem : SharedPhysicsSystem
    {
        private static readonly Gauge AwakeBodiesGauge = Metrics.CreateGauge(
            "robust_server_physics_awake_bodies",
            "Number of physics bodies currently awake on the server.");

        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        public override void Initialize()
        {
            base.Initialize();
            LoadMetricCVar();

            Subs.CVar(_configurationManager, CVars.MetricsEnabled, _ => LoadMetricCVar());
        }

        private void LoadMetricCVar()
        {
            MetricsEnabled = _configurationManager.GetCVar(CVars.MetricsEnabled);
        }

        /// <inheritdoc />
        public override void Update(float frameTime)
        {
            SimulateWorld(frameTime, false);
            AwakeBodiesGauge.Set(AwakeBodies.Count);
        }
    }
}
