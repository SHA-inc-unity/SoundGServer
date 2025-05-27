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
                //WebSocketServerExample.PrintLimited(dbConnection.ConnectionString);

                int senderId = player.Id;

                if (dbConnection.State != ConnectionState.Open)
                {
                    WebSocketServerExample.PrintLimited("DB connection error");

                    return;
                }

                //WebSocketServerExample.PrintLimited(sqlCommand);

                try
                {
                    // Определение типа SQL-команды
                    switch (sqlCommand)
                    {
                        case string s when s.StartsWith("Login"):
                            //OK
                            await Task.Run(() => Login(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("Register"):
                            //OK
                            await Task.Run(() => Register(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("GetTopSongs"):
                            //OK
                            await Task.Run(() => GetTopSongs(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("SaveSong"):
                            //
                            await Task.Run(() => SaveSong(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("UploadSongPart"):
                            //
                            await Task.Run(() => UploadSongPart(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("DownloadSongPart"):
                            //
                            await Task.Run(() => DownloadSongPart(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("DownloadSong"):
                            //
                            await Task.Run(() => DownloadSong(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("GetPrize"):
                            //
                            await Task.Run(() => GetPrize(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("GetMeatCoin"):
                            await Task.Run(() => GetMeatCoin(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        default:
                            WebSocketServerExample.PrintLimited("Command not found");
                            break;
                    }
                }
                catch (Exception e)
                {
                    WebSocketServerExample.PrintLimited($"Error executing SQL command: {e}");
                }
            }
        }


        private async Task Login(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем команду
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "Login"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string hashedPassword = credentials[2];

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                SELECT 
                    UserName, MeatCoin
                FROM 
                    UserTable 
                WHERE 
                    UserName = @login AND Password = @password";

                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            string userName = reader.GetString(0);
                            int meatCoin = reader.GetInt32(1);

                            // Отправляем успешный логин
                            string result = $"true {userName} {meatCoin}";
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
                WebSocketServerExample.PrintLimited($"Error in Login command: {e}");
            }
        }

        private async Task Register(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем команду
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "Register"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string email = credentials[2];
                string hashedPassword = credentials[3];

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                INSERT INTO UserTable (UserName, Password, EmailAddress) 
                VALUES (@login, @password, @email)";

                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", hashedPassword);
                    cursor.Parameters.AddWithValue("email", email);

                    try
                    {
                        await cursor.ExecuteNonQueryAsync();

                        // Успешная регистрация
                        string result = "true Registration successful";
                        lobby.SendMessagePlayer(result, ws, requestId);
                    }
                    catch (PostgresException ex) when (ex.SqlState == "23505") // Код ошибки уникальности
                    {
                        // Логин или email уже существуют
                        string result = "false UserName or Email already exists";
                        lobby.SendMessagePlayer(result, ws, requestId);
                    }
                }
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in Register command: {e}");
            }
        }

        private async Task GetMeatCoin(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                List<string> parts = new(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "GetMeatCoin"

                int requestId = int.Parse(parts[0]);
                string username = parts[1];

                using (var cmd = dbConnection.CreateCommand())
                {
                    cmd.CommandText = "SELECT MeatCoin FROM UserTable WHERE UserName = @username";
                    cmd.Parameters.AddWithValue("username", username);

                    object result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        lobby.SendMessagePlayer(result.ToString(), ws, requestId);
                    }
                    else
                    {
                        lobby.SendMessagePlayer("0", ws, requestId); // fallback
                    }
                }
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in GetMeatCoin: {e}");
                lobby.SendMessagePlayer("0", ws, 0);
            }
        }


        private async Task GetTopSongs(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем входные данные
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем "GetTopSongs"

                int requestId = int.Parse(credentials[0]);
                string login = credentials[1];
                string hashedPassword = credentials[2];

                // Проверяем логин и пароль
                string userQuery = @"
            SELECT UserName 
            FROM UserTable 
            WHERE UserName = @login AND Password = @password";

                using (var userCmd = dbConnection.CreateCommand())
                {
                    userCmd.CommandText = userQuery;
                    userCmd.Parameters.AddWithValue("login", login);
                    userCmd.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await userCmd.ExecuteReaderAsync())
                    {
                        if (!reader.Read())
                        {
                            string result = "false Invalid login or password";
                            lobby.SendMessagePlayer(result, ws, requestId);
                            return;
                        }
                    }
                }

                // Получаем топ-100 песен по BuyCount, а также проверяем их статус для пользователя
                string topSongsQuery = @"
            SELECT s.SongName, s.Price, s.BuyCount, 
                   COALESCE(ut.OwnType, 'load') AS OwnType
            FROM Songs s
            LEFT JOIN UserToSong ut ON s.SongName = ut.SongName AND ut.UserName = @login
            ORDER BY s.BuyCount DESC
            LIMIT 100";

                List<string> topSongs = new List<string>();

                using (var topSongCmd = dbConnection.CreateCommand())
                {
                    topSongCmd.CommandText = topSongsQuery;
                    topSongCmd.Parameters.AddWithValue("login", login);

                    using (var topSongReader = await topSongCmd.ExecuteReaderAsync())
                    {
                        while (topSongReader.Read())
                        {
                            string songName = topSongReader.GetString(0);
                            int price = topSongReader.GetInt32(1);
                            int buyCount = topSongReader.GetInt32(2);
                            string ownType = topSongReader.GetString(3); // buyed, owner, или load
                            topSongs.Add($"{songName}__{price}__{buyCount}__{ownType}");
                        }
                    }
                }

                // Формируем итоговый список
                string finalResult = $"true {string.Join(" ", topSongs)}";
                lobby.SendMessagePlayer(finalResult, ws, requestId);
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in GetTopSongs command: {e}");
            }
        }





        private async Task SaveSong(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Разбираем команду
                List<string> parts = new List<string>(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "SaveSong"

                int requestId = int.Parse(parts[0]);
                string username = parts[1];
                string hashedPassword = parts[2];
                WebSocketServerExample.PrintLimited(parts[3]);
                int partsCount = int.Parse(parts[3]);
                string songname = parts[4];
                string muzPackPreview = parts[5];

                using (var cursor = dbConnection.CreateCommand())
                {
                    // Проверяем пользователя
                    cursor.CommandText = @"
            SELECT UserName FROM UserTable 
            WHERE UserName = @username AND Password = @password";

                    cursor.Parameters.AddWithValue("username", username);
                    cursor.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (!reader.Read())
                        {
                            lobby.SendMessagePlayer("false Invalid credentials", ws, requestId);
                            return;
                        }
                    }

                    // Сохраняем песню в таблицу songs
                    string filePath = $"uploads/{songname}.zip";

                    cursor.CommandText = @"
            INSERT INTO songs (songname, linktosong, price, buycount, preview) 
            VALUES (@songname, @linktosong, 0, 0, @preview)
            ON CONFLICT (songname) DO NOTHING;";

                    cursor.Parameters.AddWithValue("songname", songname);
                    cursor.Parameters.AddWithValue("linktosong", filePath);
                    cursor.Parameters.AddWithValue("preview", muzPackPreview);

                    await cursor.ExecuteNonQueryAsync();

                    // Связываем пользователя с песней
                    cursor.CommandText = @"
            INSERT INTO usertosong (username, songname, owntype, userscore) 
            VALUES (@username, @songname, 'owner', 0)
            ON CONFLICT DO NOTHING;";

                    await cursor.ExecuteNonQueryAsync();

                    lobby.SendMessagePlayer($"true {songname}", ws, requestId);
                }
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in SaveSong command: {e}");
            }
        }

        private async Task UploadSongPart(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                List<string> parts = new List<string>(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "UploadSongPart"

                int requestId = int.Parse(parts[0]); // ID запроса
                string songName = parts[1];
                string songAuthor = parts[2];
                int partNumber = int.Parse(parts[3]); // Номер части
                int totalParts = int.Parse(parts[4]); // Всего частей
                string encodedData = parts[5]; // Закодированные данные (base64 или hex)

                // Декодируем данные
                byte[] fileChunk = Convert.FromBase64String(encodedData); // Если hex: Convert.FromHexString(encodedData)

                string basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../uploads"));
                string songDir = Path.Combine(basePath, $"song_{songName}_{songAuthor}");
                Directory.CreateDirectory(songDir); // Создаём папку, если её нет

                string partFilePath = Path.Combine(songDir, $"part_{songName}_{songAuthor}_{partNumber}.bin");

                // Записываем часть файла
                await File.WriteAllBytesAsync(partFilePath, fileChunk);

                Player? foundPlayer = lobby.FindPlayerById(senderId);
                if (foundPlayer != null)
                {
                    Console.WriteLine($"Игрок найден! ID: {foundPlayer.Id}");
                }
                else
                {
                    Console.WriteLine("Игрок не найден.");
                }

                if (!foundPlayer.loadedParts.ContainsKey($"{songName}_{songAuthor}"))
                {
                    foundPlayer.loadedParts[$"{songName}_{songAuthor}"] = (1, totalParts);
                }
                else
                {
                    var (current, total) = foundPlayer.loadedParts[$"{songName}_{songAuthor}"];
                    foundPlayer.loadedParts[$"{songName}_{songAuthor}"] = (current + 1, total);
                }

                if (foundPlayer.loadedParts[$"{songName}_{songAuthor}"].Item1 == totalParts) // Если загружены все части, собираем файл
                {
                    UploadSongg(songName, songAuthor, totalParts, basePath, songDir, dbConnection);
                    lobby.SendMessagePlayer($"true {songName}", ws, requestId);
                }
                else
                {
                    lobby.SendMessagePlayer($"true {partNumber}", ws, requestId);
                }
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in UploadSongPart command: {e}");
            }
        }

        private async Task GetPrize(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                List<string> parts = new List<string>(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "GetPrize"

                int requestId = int.Parse(parts[0]);
                string username = parts[1];
                string songName = parts[2];
                float scoreP = float.Parse(parts[3], CultureInfo.InvariantCulture);

                int prize = scoreP switch
                {
                    < 0.7f => 1,
                    < 0.9f => 3,
                    _ => 5
                };

                int scorePercent = (int)(scoreP * 100);

                // Обновляем или вставляем результат для песни
                using (var cmd = dbConnection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO UserToSong (username, songname, owntype, userscore)
                        VALUES (@username, @songname, 'standart', @newScore)
                        ON CONFLICT (username, songname)
                        DO UPDATE SET userscore = GREATEST(UserToSong.userscore, @newScore);";

                    cmd.Parameters.AddWithValue("username", username);
                    cmd.Parameters.AddWithValue("songname", songName);
                    cmd.Parameters.AddWithValue("newScore", scorePercent);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Повышаем MeatCoin
                using (var updateCoin = dbConnection.CreateCommand())
                {
                    updateCoin.CommandText = @"
                        UPDATE UserTable
                        SET MeatCoin = MeatCoin + @prize
                        WHERE UserName = @username";

                    updateCoin.Parameters.AddWithValue("prize", prize);
                    updateCoin.Parameters.AddWithValue("username", username);

                    await updateCoin.ExecuteNonQueryAsync();
                }

                lobby.SendMessagePlayer($"{prize}", ws, requestId);
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in GetPrize: {e}");
                lobby.SendMessagePlayer("0", ws, 0);
            }
        }



        private async Task DownloadSong(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                List<string> parts = new List<string>(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "DownloadSong"

                int requestId = int.Parse(parts[0]);
                string username = parts[1];
                string hashedPassword = parts[2];
                string songName = parts[3];

                // Проверка логина
                using (var authCmd = dbConnection.CreateCommand())
                {
                    authCmd.CommandText = @"
                        SELECT 1 FROM UserTable 
                        WHERE UserName = @username AND Password = @password";
                    authCmd.Parameters.AddWithValue("username", username);
                    authCmd.Parameters.AddWithValue("password", hashedPassword);

                    using (var reader = await authCmd.ExecuteReaderAsync())
                    {
                        if (!reader.Read())
                        {
                            lobby.SendMessagePlayer("false Invalid credentials", ws, requestId);
                            return;
                        }
                    }
                }

                // Проверка доступа к песне и получение пути
                string? filePath = null;
                using (var checkCmd = dbConnection.CreateCommand())
                {
                    checkCmd.CommandText = @"
                        SELECT s.linktosong FROM Songs s
                        JOIN UserToSong u ON s.SongName = u.SongName
                        WHERE u.UserName = @username AND u.SongName = @songname";
                    checkCmd.Parameters.AddWithValue("username", username);
                    checkCmd.Parameters.AddWithValue("songname", songName);

                    using (var reader = await checkCmd.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            filePath = reader.GetString(0);
                        }
                        else
                        {
                            lobby.SendMessagePlayer("false Access denied", ws, requestId);
                            return;
                        }
                    }
                }

                if (!File.Exists(filePath))
                {
                    lobby.SendMessagePlayer("false File not found", ws, requestId);
                    return;
                }

                const int bufferSize = 64 * 1024; // 64 KB
                long fileSize = new FileInfo(filePath).Length;
                int totalParts = (int)Math.Ceiling((double)fileSize / bufferSize);

                lobby.SendMessagePlayer($"true {totalParts}", ws, requestId);
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in DownloadSong: {e}");
            }
        }


        private async Task DownloadSongPart(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                List<string> parts = new List<string>(sqlCommand.Split(' '));
                parts.RemoveAt(0); // Убираем "DownloadSongPart"

                int requestId = int.Parse(parts[0]);
                string songName = parts[1];
                string username = parts[2];
                int partNumber = int.Parse(parts[3]);
                int totalParts = int.Parse(parts[4]);

                // Получение пути к файлу
                string? filePath = null;
                using (var cmd = dbConnection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT s.linktosong FROM Songs s
                        JOIN UserToSong u ON s.SongName = u.SongName
                        WHERE u.UserName = @username AND u.SongName = @songname";
                    cmd.Parameters.AddWithValue("username", username);
                    cmd.Parameters.AddWithValue("songname", songName);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            filePath = reader.GetString(0);
                        }
                        else
                        {
                            lobby.SendMessagePlayer("false Access denied", ws, requestId);
                            return;
                        }
                    }
                }

                if (!File.Exists(filePath))
                {
                    lobby.SendMessagePlayer("false File not found", ws, requestId);
                    return;
                }

                const int bufferSize = 64 * 1024; // 64 KB
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek((long)partNumber * bufferSize, SeekOrigin.Begin);

                    byte[] buffer = new byte[Math.Min(bufferSize, (int)(fs.Length - fs.Position))];
                    int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);

                    string chunkBase64 = Convert.ToBase64String(buffer, 0, bytesRead);
                    lobby.SendMessagePlayer($"true {partNumber} {chunkBase64}", ws, requestId);
                }
            }
            catch (Exception e)
            {
                WebSocketServerExample.PrintLimited($"Error in DownloadSongPart: {e}");
            }
        }



        private async Task UploadSongg(string songName, string songAuthor, int totalParts, string basePath, string songDir, NpgsqlConnection dbConnection)
        {
            await Task.Delay(5000); // 2000 миллисекунд = 2 секунды

            string finalFilePath = Path.Combine(basePath, $"song_{songName}_{songAuthor}.muzpack");

            using (FileStream finalFile = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < totalParts; i++)
                {
                    string chunkPath = Path.Combine(songDir, $"part_{songName}_{songAuthor}_{i}.bin");
                    if (File.Exists(chunkPath))
                    {
                        byte[] chunk = await File.ReadAllBytesAsync(chunkPath);
                        await finalFile.WriteAsync(chunk, 0, chunk.Length);
                        File.Delete(chunkPath); // Удаляем часть после записи
                        WebSocketServerExample.PrintLimited($"rmc              {chunkPath}");
                    }
                    else
                    {
                        WebSocketServerExample.PrintLimited($"Missing chunk: {chunkPath}");
                        return;
                    }
                }
            }

            // Удаляем временную папку
            Directory.Delete(songDir);

            // Обновляем путь в БД
            using (var cursor = dbConnection.CreateCommand())
            {
                cursor.CommandText = @"
                            UPDATE songs 
                            SET linktosong = @linktosong 
                            WHERE songname = @songname;";

                cursor.Parameters.AddWithValue("linktosong", finalFilePath);
                cursor.Parameters.AddWithValue("songname", $"{songName}");

                await cursor.ExecuteNonQueryAsync();
            }
        }

    }
}
