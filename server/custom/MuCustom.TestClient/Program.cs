// Headless game client for testing the MuCustom features without the game:
// logs in, enters the world with a character, sends chat commands and prints what the server answers.
//
//   dotnet run -- --user test0 --pass test0 --char test0Dk "/vip" "/recargar 6" "/vip comprar 2"
//
// Options: --host (127.0.0.1), --port (45901: first game server), --wait seconds per command (2),
//          --raw (also print the #MUVIP lines of the VIP window),
//          --version (20404: the open source client like MuMain; 10404: the original S6E3 client,
//          whose item packets the test client writes).
// A command "!npc N" talks to the nearest NPC of type N (e.g. "!npc 240" for the vault keeper),
// "!mover S1 slot1 S2 slot2" moves an item between storages (0 inventory, 2 vault, 3 chaos machine),
// "!mix N" combines in the chaos machine with mix type N, "!tienda NAME" opens the personal store
// "!vertienda NAME" asks for the items of a personal store in view, "!comprar NAME SLOT" buys one
// (SLOT from 204) and "!cerrar" closes the NPC window.
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.SimpleModulus;
using MUnique.OpenMU.Network.Xor;
using Pipelines.Sockets.Unofficial;

var options = Options.Parse(args);
using var tcpClient = new TcpClient(options.Host, options.Port);
var socket = SocketConnection.Create(tcpClient.Client);
var encryptor = new PipelinedXor32Encryptor(new PipelinedSimpleModulusEncryptor(socket.Output, PipelinedSimpleModulusEncryptor.DefaultClientKey).Writer);
var decryptor = new PipelinedSimpleModulusDecryptor(socket.Input, PipelinedSimpleModulusDecryptor.DefaultClientKey);
using var connection = new Connection(socket, decryptor, encryptor, new NullLogger<Connection>());

var loggedIn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var characterList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var inWorld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var hello = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var npcIdsByType = new System.Collections.Concurrent.ConcurrentDictionary<ushort, ushort>();
var playerIdsByName = new System.Collections.Concurrent.ConcurrentDictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

connection.PacketReceived += sequence =>
{
    var packet = sequence.ToArray();
    Handle(packet);
    return ValueTask.CompletedTask;
};
connection.Disconnected += () =>
{
    Console.WriteLine("<< desconectado");
    loggedIn.TrySetResult(false);
    return ValueTask.CompletedTask;
};

_ = connection.BeginReceiveAsync();
await WaitAsync(hello.Task, "saludo del servidor");

var user = Xor3(options.User, 10);
var password = Xor3(options.Password, 20);
await connection.SendLoginLongPasswordAsync(user, password, (uint)Environment.TickCount, Encoding.ASCII.GetBytes(options.Version), Encoding.ASCII.GetBytes("k1Pk2jcET48mxL3b"));
if (!await WaitForAsync(loggedIn.Task, "login"))
{
    return 1;
}

await connection.SendRequestCharacterListAsync(0);
await WaitAsync(characterList.Task, "lista de personajes");
await connection.SendSelectCharacterAsync(options.Character);
await WaitAsync(inWorld.Task, "entrada al mundo");
await Task.Delay(1000);
await connection.SendClientReadyAfterMapChangeAsync();
await Task.Delay(1500);

foreach (var command in options.Commands)
{
    Console.WriteLine();
    Console.WriteLine($">> {command}");
    if (command.StartsWith("!npc ", StringComparison.Ordinal))
    {
        var type = ushort.Parse(command[5..]);
        if (npcIdsByType.TryGetValue(type, out var npcId))
        {
            await connection.SendTalkToNpcRequestAsync(npcId);
        }
        else
        {
            Console.WriteLine($"<< no hay un NPC {type} a la vista (vistos: {string.Join(", ", npcIdsByType.Keys.Order())})");
        }
    }
    else if (command.StartsWith("!mover ", StringComparison.Ordinal))
    {
        var parts = command.Split(' ');
        await connection.SendItemMoveRequestAsync(
            (ItemStorageKind)byte.Parse(parts[1]),
            byte.Parse(parts[2]),
            new byte[12],
            (ItemStorageKind)byte.Parse(parts[3]),
            byte.Parse(parts[4]));
    }
    else if (command.StartsWith("!mix ", StringComparison.Ordinal))
    {
        await connection.SendChaosMachineMixRequestAsync((ChaosMachineMixRequest.ChaosMachineMixType)byte.Parse(command[5..]), 0);
    }
    else if (command.StartsWith("!vertienda ", StringComparison.Ordinal) || command.StartsWith("!comprar ", StringComparison.Ordinal))
    {
        var parts = command.Split(' ');
        if (!playerIdsByName.TryGetValue(parts[1], out var playerId))
        {
            Console.WriteLine($"<< {parts[1]} no estÃ¡ a la vista (vistos: {string.Join(", ", playerIdsByName.Keys)})");
        }
        else if (parts[0] == "!vertienda")
        {
            await connection.SendPlayerShopItemListRequestAsync(playerId, parts[1]);
        }
        else
        {
            await connection.SendPlayerShopItemBuyRequestAsync(playerId, parts[1], byte.Parse(parts[2]));
        }
    }
    else if (command.StartsWith("!tienda ", StringComparison.Ordinal))
    {
        await connection.SendPlayerShopOpenAsync(command[8..]);
    }
    else if (command == "!cerrar")
    {
        await connection.SendCloseNpcRequestAsync();
    }
    else
    {
        await connection.SendPublicChatMessageAsync(options.Character, command);
    }

    await Task.Delay(TimeSpan.FromSeconds(options.WaitSeconds));
}

await Task.Delay(500);
return 0;

void Handle(byte[] packet)
{
    if (packet.Length < 3)
    {
        return;
    }

    var headerSize = packet[0] is 0xC1 or 0xC3 ? 2 : 3;
    var code = packet[headerSize];
    var subCode = packet.Length > headerSize + 1 ? packet[headerSize + 1] : (byte)0;
    switch (code)
    {
        case 0xF1 when subCode == 0x00:
            hello.TrySetResult();
            break;
        case 0xF1 when subCode == 0x01:
            var result = ((LoginResponseRef)packet.AsSpan()).Success;
            Console.WriteLine($"<< login: {result}");
            loggedIn.TrySetResult(result == LoginResponse.LoginResult.Okay);
            break;
        case 0xF3 when subCode == 0x00:
            characterList.TrySetResult();
            break;
        case 0xF3 when subCode == 0x03:
            Console.WriteLine($"<< en el mundo con {options.Character}");
            inWorld.TrySetResult();
            break;
        case 0x0D:
            var message = ((ServerMessageRef)packet.AsSpan()).Message.TrimStart('0');
            if (options.Raw || !message.StartsWith("#MUVIP|", StringComparison.Ordinal))
            {
                Console.WriteLine($"<< {message}");
            }
            else if (message == "#MUVIP|E")
            {
                Console.WriteLine("<< (datos de la ventana VIP recibidos)");
            }

            break;
        case 0x13 when packet[0] == 0xC2:
            var npcs = (AddNpcsToScopeRef)packet.AsSpan();
            for (var i = 0; i < npcs.NpcCount; i++)
            {
                var npc = npcs[i];
                npcIdsByType[npc.TypeNumber] = npc.Id;
            }

            break;
        case 0x31 when packet[0] == 0xC2:
            var list = (StoreItemListRef)packet.AsSpan();
            Console.WriteLine($"<< ventana {list.Type}: {list.ItemCount} items");
            break;
        case 0x12 when packet[0] == 0xC2:
            var characters = (AddCharactersToScopeRef)packet.AsSpan();
            if (characters.CharacterCount > 0)
            {
                var character = characters[0];
                playerIdsByName[character.Name] = (ushort)(character.Id & 0x7FFF);
            }

            break;
        case 0x3F:
            Console.WriteLine($"<< tienda personal: subcÃ³digo {subCode:X2}, {packet.Length} bytes");
            break;
        case 0x86:
            Console.WriteLine($"<< resultado de la Chaos Machine: {packet[headerSize + 1]} (1 = exito, 0 = fallo)");
            break;
        case 0x24:
            Console.WriteLine($"<< item movido: resultado {packet[headerSize + 1]:X2} -> slot {packet[headerSize + 2]}");
            break;
        case 0x00:
            var chat = (ChatMessageRef)packet.AsSpan();
            Console.WriteLine($"<< [chat] {chat.Sender}: {chat.Message}");
            break;
    }
}

static byte[] Xor3(string text, int length)
{
    var bytes = new byte[length];
    Encoding.UTF8.GetBytes(text, bytes);
    new Xor3Encryptor(0).Encrypt(bytes);
    return bytes;
}

static async Task<T> WaitForAsync<T>(Task<T> task, string what)
{
    if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))) != task)
    {
        throw new TimeoutException($"Sin respuesta del servidor: {what}.");
    }

    return await task;
}

static async Task WaitAsync(Task task, string what)
{
    if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15))) != task)
    {
        throw new TimeoutException($"Sin respuesta del servidor: {what}.");
    }
}

internal sealed record Options(string Host, int Port, string User, string Password, string Character, double WaitSeconds, bool Raw, string Version, IReadOnlyList<string> Commands)
{
    public static Options Parse(string[] args)
    {
        string host = "127.0.0.1", user = "test0", password = "test0", character = "test0Dk", version = "20404";
        int port = 45901;
        double wait = 2;
        var raw = false;
        var commands = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--host": host = args[++i]; break;
                case "--port": port = int.Parse(args[++i]); break;
                case "--user": user = args[++i]; break;
                case "--pass": password = args[++i]; break;
                case "--char": character = args[++i]; break;
                case "--wait": wait = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--raw": raw = true; break;
                case "--version": version = args[++i]; break;
                default: commands.Add(args[i]); break;
            }
        }

        return new Options(host, port, user, password, character, wait, raw, version, commands);
    }
}
