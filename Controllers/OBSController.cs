using System.Threading.Tasks;
using OBSWebsocketDotNet;

namespace FaraBotModerator.Controllers;

// Twitchである程度制御できるからもう不要かもしれない
internal class OBSController
{
    private readonly string _ip;
    private readonly OBSWebsocket _obsWebSocket;
    private readonly string _password;
    private readonly int _port;

    public OBSController(string ip, string password, int port)
    {
        _ip = ip;
        _password = password;
        _port = port;
        _obsWebSocket = new OBSWebsocket();
        _obsWebSocket.ConnectAsync($"ws://{_ip}:{_port}", _password);
    }

    public void SwitchEvent()
    {
        var targetSceneName = "sceneName";
        var targetSourceName = "sourceName";
        var sceneItemId = _obsWebSocket.GetSceneItemId(targetSceneName, targetSourceName, -1);
        _obsWebSocket.SetSceneItemEnabled(targetSceneName, sceneItemId, true);
        Task.Delay(10);
        _obsWebSocket.SetSceneItemEnabled(targetSceneName, sceneItemId, false);
    }

    public void Disconnect()
    {
        _obsWebSocket.Disconnect();
    }
}