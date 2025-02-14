using System.Text;
using System.Net.WebSockets;
using Npgsql;
using System.Security.Cryptography;
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Specialized;
using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Net;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;


namespace shooter_server
{
    public class SqlCommander
    {
        private string host;
        private string user;
        private string password;
        private string database;
        private int port;


        public SqlCommander(string host, string user, string password, string database, int port)
        {
            this.host = host;
            this.user = user;
            this.password = password;
            this.database = database;
            this.port = port;
        }


        public async Task ExecuteSqlCommand(Lobby lobby, WebSocket webSocket, string sqlCommand, Player player)
        {
            // Создание соединения с базой данных
            using (var dbConnection = new NpgsqlConnection($"Host={host};Username={user};Password={password};Database={database};Port={port}"))
            {
                await dbConnection.OpenAsync();
                //Console.WriteLine(dbConnection.ConnectionString);

                int senderId = player.Id;

                if (dbConnection.State != ConnectionState.Open)
                {
                    Console.WriteLine("DB connection error");

                    return;
                }

                //Console.WriteLine(sqlCommand);

                try
                {
                    // Определение типа SQL-команды
                    switch (sqlCommand)
                    {

                        case string s when s.StartsWith("SendMessage"):
                            //RW
                            await Task.Run(() => SendMessage(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("AddUserToChat"):
                            //OK
                            await Task.Run(() => AddSubuserToChat(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("SendChatUsers"):
                            //OK
                            await Task.Run(() => SendChatUsers(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("ChatCreate"):
                            //OK
                            await Task.Run(() => ChatCreate(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("DeleteChat"):
                            //RW
                            await Task.Run(() => DeleteChat(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("Login"):
                            //OK
                            await Task.Run(() => Login(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("AltRegister"):
                            //OK
                            await Task.Run(() => Register(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("GetQueueMessages"):
                            //OK
                            await Task.Run(() => GetQueueMessages(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        default:
                            Console.WriteLine("Command not found");
                            break;
                    }
                }
                catch (Exception e)
                {
                    //Console.WriteLine($"Error executing SQL command: {e}");
                }
            }
        }


        // Удаление чата со всеми пользователями и всеми сообщениями
        private async Task DeleteChat(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    // Разбираем команду
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));
                    credentials.RemoveAt(0);

                    int requestId = int.Parse(credentials[0]);
                    string chatId = credentials[1];

                    // Удаление из chatqueue
                    cursor.CommandText = @"DELETE FROM chatqueue WHERE chatid = @chatId;";
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    await cursor.ExecuteNonQueryAsync();

                    // Удаление всех пользователей (subuser)
                    cursor.CommandText = @"DELETE FROM subuser WHERE chatid = @chatId;";
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    await cursor.ExecuteNonQueryAsync();

                    // Удаление чата
                    cursor.CommandText = @"DELETE FROM chat WHERE chatid = @chatId;";
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    await cursor.ExecuteNonQueryAsync();

                    // Отправляем сообщение о завершении операции
                    lobby.SendMessagePlayer($"true", ws, requestId);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error DeleteChat: {e}");
            }
        }



        // Добавить подюзера в чат
        private async Task AddSubuserToChat(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    // addSubuserToChat requestId chatId privateKey publicKey anonId
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));
                    credentials.RemoveAt(0);

                    int requestId = int.Parse(credentials[0]);

                    byte[] publicKey = Encoding.UTF8.GetBytes(credentials[1]);

                    byte[] privateKey = Encoding.UTF8.GetBytes(credentials[2]);

                    string chatId = credentials[3];

                    byte[] unicalcode = Encoding.UTF8.GetBytes(credentials[4]);

                    cursor.Parameters.AddWithValue("chatid", chatId);

                    // Проверка существования чата
                    cursor.CommandText = @"SELECT chatid FROM chat WHERE chatid = @chatid;";

                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            reader.Close();

                            // Создание нового подюзера
                            int subuserId = GenerateUniqueSubuserId(dbConnection);
                            string username = "std";

                            cursor.Parameters.AddWithValue("subuserid", subuserId);
                            cursor.Parameters.AddWithValue("privatekey", privateKey);
                            cursor.Parameters.AddWithValue("publickey", publicKey);
                            cursor.Parameters.AddWithValue("username", username);
                            cursor.Parameters.AddWithValue("unicalcode", unicalcode);

                            cursor.CommandText = @"
                                INSERT INTO subuser (chatid, subuserid, unicalcode, username, privatekey, publickey)
                                VALUES (@chatid, @subuserid, @unicalcode, @username, @privatekey, @publickey);";

                            await cursor.ExecuteNonQueryAsync();

                            // Отправка подтверждения
                            lobby.SendMessagePlayer($"true {subuserId.ToString()}", ws, requestId);
                        }
                        else
                        {
                            // Логирование или отправка сообщения об ошибке
                            Console.WriteLine("Chat not found.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Обработка исключений
                Console.WriteLine($"Error in AddSubuserToChat command: {e}");
            }
        }

        private async Task SendChatUsers(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));
                    credentials.RemoveAt(0);

                    int requestId = int.Parse(credentials[0]);
                    string chatId = credentials[1];

                    cursor.CommandText = @"
                        SELECT subuserid, privatekey 
                        FROM subuser
                        WHERE chatid = @chatid;";

                    cursor.Parameters.AddWithValue("chatid", chatId);

                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        List<string> usersData = new List<string>();

                        while (await reader.ReadAsync())
                        {
                            int subuserId = reader.GetInt32(0);
                            byte[] privateKeyBytes = reader["privatekey"] as byte[];
                            string privateKey = Encoding.UTF8.GetString(privateKeyBytes);
                            usersData.Add($"{subuserId} {privateKey}");
                        }

                        if (usersData.Count > 0)
                        {
                            string response = "true " + string.Join(" ", usersData);
                            lobby.SendMessagePlayer(response, ws, requestId);
                        }
                        else
                        {
                            lobby.SendMessagePlayer("", ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in SendChatUsersWithPrivateKeys: {e}");
            }
        }


        private int GenerateUniqueSubuserId(NpgsqlConnection dbConnection)
        {
            int newId = -1;
            bool isUnique = false;

            while (!isUnique)
            {
                // Генерируем случайное число для SubuserId
                newId = new Random().Next(1, int.MaxValue);

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"SELECT COUNT(*) FROM subuser WHERE subuserid = @subuserId;";
                    cursor.Parameters.AddWithValue("subuserId", newId);

                    var count = cursor.ExecuteScalar();

                    // Если ID уникален, выходим из цикла
                    if (((long)count) == 0)
                    {
                        isUnique = true;
                    }
                }
            }

            return newId;
        }

        private async Task SendMessage(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем входные данные
                List<string> credentials = new List<string>(sqlCommand.Split(' '));

                credentials.RemoveAt(0); // Убираем "SendMessage"

                int requestId = int.Parse(credentials[0]);  // requestId
                string chatId = credentials[1];             // chatId
                int idSender = int.Parse(credentials[2]);             // chatId
                byte[] msg;

                try
                {
                    msg = Convert.FromBase64String(credentials[3]); // Декодируем сообщение
                }
                catch (FormatException)
                {
                    lobby.SendMessagePlayer($"false Invalid Base64 encoding", ws, requestId);
                    return;
                }

                // Получаем последний changeId в данном чате
                long changeId;
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    cursor.CommandText = "SELECT COALESCE(MAX(changeid), 0) FROM chatqueue WHERE chatid = @chatId;";

                    object result = await cursor.ExecuteScalarAsync();
                    changeId = (result != DBNull.Value) ? (long)result + 1 : 1;
                }

                // Вставляем новое сообщение
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = "INSERT INTO chatqueue (chatid, changeid, changedata, senderid) VALUES (@chatId, @changeId, @msg, @senderid)";
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    cursor.Parameters.AddWithValue("changeId", changeId);
                    cursor.Parameters.AddWithValue("msg", msg);
                    cursor.Parameters.AddWithValue("senderid", idSender);

                    await cursor.ExecuteNonQueryAsync();
                }

                // Подтверждаем успешную отправку
                lobby.SendMessagePlayer("true", ws, requestId);
            }
            catch (Exception e)
            {
                lobby.SendMessagePlayer($"false {e.Message}", ws, 0);
            }
        }

        public async Task ChatCreate(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    // sqlCommand: "ChatCreate requestId isPrivacy chatPassword"
                    var credentials = sqlCommand.Split(' ').ToList();

                    credentials.RemoveAt(0); // Remove "ChatCreate"

                    int requestId = int.Parse(credentials[0]);
                    string chatPassword = credentials[1];
                    bool isPrivacy = bool.Parse(credentials[2]);

                    string chatId = GenerateUniqueChatId(dbConnection);

                    cursor.CommandText = "INSERT INTO chat (chatid, password, isgroup) VALUES (@chatId, @chatPassword, @isGroup);";
                    cursor.Parameters.AddWithValue("chatId", chatId);
                    cursor.Parameters.AddWithValue("chatPassword", Encoding.UTF8.GetBytes(chatPassword));
                    cursor.Parameters.AddWithValue("isGroup", isPrivacy);

                    await cursor.ExecuteNonQueryAsync();

                    // Отправка сообщения о создании чата клиенту
                    lobby.SendMessagePlayer($"true {chatId}", ws, requestId);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in ChatCreate: {e.Message}");
            }
        }

        private string GenerateUniqueChatId(NpgsqlConnection dbConnection)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            string chatId = "";
            bool isUnique = false;

            Random random = new Random(); // Создаем объект Random для генерации случайных чисел

            while (!isUnique)
            {
                // Генерация 64 случайных символов
                char[] chatIdChars = new char[64];

                for (int i = 0; i < chatIdChars.Length; i++)
                {
                    // Выбор случайного символа из chars
                    chatIdChars[i] = chars[random.Next(chars.Length)];
                }

                chatId = new string(chatIdChars);

                // Проверка на уникальность в базе данных
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = "SELECT COUNT(*) FROM chat WHERE chatid = @chatId;";
                    cursor.Parameters.AddWithValue("chatId", chatId);

                    var result = cursor.ExecuteScalar();
                    isUnique = result != null && Convert.ToInt32(result) == 0;
                }
            }

            return chatId; // Возвращаем уникальный идентификатор в строковом формате
        }

        private async Task Login(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Извлекаем параметры запроса: requestId, login и password
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем саму команду из запроса

                int requestId = int.Parse(credentials[0]);
                byte[] login = Convert.FromBase64String(credentials[1]);
                byte[] password = Convert.FromBase64String(credentials[2]);

                // Выполняем запрос для получения AnonId по логину и паролю
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                        SELECT 
                            a.AnonId 
                        FROM 
                            Anon a
                        WHERE 
                            a.Login = @login AND a.Password = @password";

                    // Добавляем параметры запроса
                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", password);

                    // Выполняем запрос и читаем результат
                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            int anonId = reader.GetInt32(0);

                            // Отправляем AnonId в ответ на успешный логин
                            string result = $"true {anonId}";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                        else
                        {
                            // Если логин или пароль неверны
                            string result = "false Invalid login or password";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error SendMessage command: {e}");
            }
        }

        private async Task GetQueueMessages(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем входные параметры
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0);

                int requestId = int.Parse(credentials[0]);
                string nowChatId = credentials[1];
                int queueId = int.Parse(credentials[2]); // Преобразуем queueId в int

                // SQL-запрос на получение данных из chatqueue
                using (var getMessagesCmd = dbConnection.CreateCommand())
                {
                    getMessagesCmd.CommandText = @"
                        SELECT changeid, changedata, senderid
                        FROM chatqueue 
                        WHERE chatid = @nowChatId AND changeid > @queueId
                        ORDER BY changeid ASC"; // Сортировка по возрастанию changeid

                    getMessagesCmd.Parameters.AddWithValue("nowChatId", nowChatId);
                    getMessagesCmd.Parameters.AddWithValue("queueId", queueId);

                    // Выполняем запрос
                    using (var reader = await getMessagesCmd.ExecuteReaderAsync())
                    {
                        List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();

                        while (await reader.ReadAsync())
                        {
                            int changeId = reader.GetInt32(0);
                            byte[] changeData = reader.GetFieldValue<byte[]>(1);
                            int senderid = reader.GetInt32(2);
                            // Преобразуем changeData в строку, если это текст
                            string changeDataString = Convert.ToBase64String(changeData);

                            messages.Add(new Dictionary<string, object>
                            {
                                { "changeId", changeId },
                                { "changeData", changeDataString },
                                { "senderId", senderid }
                            });
                        }

                        // Отправляем результат клиенту
                        string jsonResponse = JsonSerializer.Serialize(messages);
                        lobby.SendMessagePlayer(jsonResponse, ws, requestId);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in GetQueueMessages: {e}");
            }
        }




        private async Task Register(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                Console.WriteLine($"R AA");
                // SendMessage requestId login password
                List<string> credentials = new List<string>(sqlCommand.Split(' '));

                credentials.RemoveAt(0);

                int requestId = int.Parse(credentials[0]);
                byte[] login = Convert.FromBase64String(credentials[1]);
                byte[] password = Convert.FromBase64String(credentials[2]);

                Console.WriteLine($"R A");

                // Check if the login already exists
                using (var checkCmd = dbConnection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Anon WHERE Login = @login";
                    checkCmd.Parameters.AddWithValue("login", login);

                    int count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    Console.WriteLine($"R B");

                    if (count > 0)
                    {
                        // Login already exists
                        lobby.SendMessagePlayer($"User with this login already exists", ws, requestId);
                    }
                    else
                    {
                        // Insert new record
                        using (var insertCmd = dbConnection.CreateCommand())
                        {
                            insertCmd.CommandText = "INSERT INTO Anon (Login, Password) VALUES (@login, @password)";
                            insertCmd.Parameters.AddWithValue("login", login);
                            insertCmd.Parameters.AddWithValue("password", password);

                            await insertCmd.ExecuteNonQueryAsync();

                            // Registration successful
                            lobby.SendMessagePlayer($"true", ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error SendMessage command: {e}");
            }
        }

    }
}
