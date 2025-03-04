using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Net.NetworkInformation;
using System.Diagnostics;
    
namespace shooter_server
{
    class WebSocketServerExample
    {
        private static int kilobite = 1024;
        private static int bufferSize = kilobite * kilobite * 64;
        private static Lobby mainLobby = new Lobby();

        static async Task Main()
        {
            PrintLimited("START");
            int port = 17925;
            HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://+:{port}/");
            httpListener.Start();

            PrintLimited($"WebSocket Server started on port {port}");
            PrintLimited("Waiting for connections...");

            while (true)
            {
                var context = await httpListener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    Task.Run(() => ProcessWebSocketRequest(context));
                }
            }
        }

        private static async Task ProcessWebSocketRequest(HttpListenerContext context)
        {
            try {
                var webSocketContext = await context.AcceptWebSocketAsync(null);
                var webSocket = webSocketContext.WebSocket;

                PrintLimited($"WebSocket connection established from: {context.Request.RemoteEndPoint}");

                mainLobby.AddPlayer(webSocket, new Player());

                await NotifyClients($"{context.Request.RemoteEndPoint} has joined.");

                byte[] buffer = new byte[bufferSize];
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        PrintLimited($"Received: {message}");

                        if (message.StartsWith("/sql"))
                        {
                            message = message.Substring("/sql".Length).Trim();
                            Task.Run(() => mainLobby.SqlCommander.ExecuteSqlCommand(mainLobby, webSocket, message, mainLobby.Players[webSocket]));
                        }
                    }
                } while (!result.CloseStatus.HasValue || result.CloseStatus != WebSocketCloseStatus.NormalClosure);
                
                await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);

                mainLobby.RemovePlayer(webSocket);

                await NotifyClients($"{context.Request.RemoteEndPoint} has left. Reason: {result.CloseStatusDescription}");

                PrintLimited($"WebSocket connection closed from: {context.Request.RemoteEndPoint}. Close status: {result.CloseStatus}, Reason: {result.CloseStatusDescription}");
            }
            catch (Exception e)
            {
                PrintLimited($"An error occurred while reading from WebSocket: {e}");
            }
        }

        private static async Task NotifyClients(string message)
        {
            foreach (var client in mainLobby.Players.Keys)
            {
                if (client.State != WebSocketState.Open)
                    continue;
                try
                {
                    await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message), 0, message.Length),
                                           WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    PrintLimited($"Error sending message to client: {ex.Message}");
                }
            }
        }

        private static async void PrintLimited(string message)
        {
            PrintLimited(message.Length > 100 ? message.Substring(0, 100) : message);
        }
    }
}
