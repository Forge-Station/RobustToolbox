using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using NUnit.Framework;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Robust.UnitTesting.Server.GameStates;

public sealed class PvsDirtyBufferResizeTest : RobustIntegrationTest
{
    [Test]
    public async Task ResizeDirtyBufferAtRuntime()
    {
        var server = StartServer();
        var client = StartClient();

        await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

        var sEntMan = server.EntMan;
        var confMan = server.ResolveDependency<IConfigurationManager>();
        var sPlayerMan = server.ResolveDependency<ISharedPlayerManager>();
        var xforms = sEntMan.System<SharedTransformSystem>();

        var cEntMan = client.EntMan;
        var cPlayerMan = client.ResolveDependency<ISharedPlayerManager>();
        var netMan = client.ResolveDependency<IClientNetManager>();

        Assert.DoesNotThrow(() => client.SetConnectTarget(server));
        client.Post(() => netMan.ClientConnect(null!, 0, null!));
        server.Post(() =>
        {
            confMan.SetCVar(CVars.NetPVS, true);
            confMan.SetCVar(CVars.NetPvsDirtyBufferSize, 1);
        });

        async Task RunTicks(int ticks = 10)
        {
            for (var i = 0; i < ticks; i++)
            {
                await server.WaitRunTicks(1);
                await client.WaitRunTicks(1);
            }
        }

        await RunTicks();

        EntityUid map = default;
        EntityUid playerUid = default;
        EntityUid otherUid = default;
        EntityCoordinates origin = default;
        await server.WaitPost(() =>
        {
            map = server.System<SharedMapSystem>().CreateMap();
            origin = new EntityCoordinates(map, default);

            playerUid = sEntMan.SpawnEntity(null, origin);
            otherUid = sEntMan.SpawnEntity(null, origin);

            var session = sPlayerMan.Sessions.First();
            server.PlayerMan.SetAttachedEntity(session, playerUid);
            sPlayerMan.JoinGame(session);
        });

        await RunTicks();

        var otherNet = sEntMan.GetNetEntity(otherUid);
        Assert.That(otherNet.IsValid(), Is.True);
        Assert.That(cPlayerMan.LocalEntity, Is.Not.Null);
        Assert.That(cEntMan.TryGetEntity(otherNet, out _), Is.True);

        await server.WaitPost(() => confMan.SetCVar(CVars.NetPvsDirtyBufferSize, 64));
        await RunTicks();

        await server.WaitPost(() =>
        {
            var moved = new EntityCoordinates(map, new Vector2(1f, 1f));
            xforms.SetCoordinates(otherUid, moved);
        });
        await RunTicks();

        Assert.That(cPlayerMan.LocalEntity, Is.Not.Null);
        Assert.That(cEntMan.TryGetEntity(otherNet, out var clientOther), Is.True);
        Assert.That(clientOther, Is.Not.Null);

        await client.WaitPost(() => netMan.ClientDisconnect(""));
        await server.WaitRunTicks(5);
        await client.WaitRunTicks(5);
    }
}
