using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    internal class Server
    {
        private TcpListener listener;
        private List<TcpClient> clients = new List<TcpClient>();
        private readonly object clientLock = new object(); // Мьютекс для безопасного доступа к списку клиентов
        private string ConnectionString = $@"Data Source=213.155.192.79,3002;Initial Catalog=BD_Messaging_System;Persist Security Info=True;User ID=u21nikip;Password=3and";
        public Server(int port)
        {
            listener = new TcpListener(IPAddress.Any, port);
        }
        public string newTcpUsers = "";
        public async Task StartAsync()
        {
            try
            {
                listener.Start();
                Console.WriteLine("Сервер запущен. Ожидание подключений...");

                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Console.WriteLine($"Подключился новый клиент: {client.Client.RemoteEndPoint}");
                    lock (clientLock)
                    {
                        clients.Add(client);
                    }

                    _ = HandleClientAsync(client); // Запускаем обработку клиента асинхронно
                    newTcpUsers = client.Client.RemoteEndPoint.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при запуске сервера: {ex.Message}");
            }
        }
        private void RemoveDisconnectedClients()
        {
            lock (clientLock)
            {
                clients.RemoveAll(client => !client.Connected);
            }
        }
        private async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    RemoveDisconnectedClients();
                    while (true)
                    {
                        byte[] sizeBuffer = new byte[4];
                        int bytesRead = await stream.ReadAsync(sizeBuffer, 0, sizeBuffer.Length);

                        int expectedSize = 4;
                        if (bytesRead != expectedSize)
                        {
                            if (client.Connected)
                            {
                                Console.WriteLine("Ошибка чтения размера сообщения.");
                                break;
                            }
                            else
                            {
                                Console.WriteLine("Клиент отключен.");
                                break;
                            }
                        }

                        int messageSize = BitConverter.ToInt32(sizeBuffer, 0);

                        byte[] messageBuffer = new byte[messageSize];
                        bytesRead = await stream.ReadAsync(messageBuffer, 0, messageBuffer.Length);

                        if (bytesRead != messageSize)
                        {
                            if (client.Connected)
                            {
                                Console.WriteLine("Ошибка чтения сообщения.");
                            }
                            break;
                        }

                        string message = Encoding.UTF8.GetString(messageBuffer);

                        // Обработка различных сообщений
                        if (message.StartsWith("#RegistrationAccount"))
                        {
                            string newMessage = message.Substring("#RegistrationAccount ".Length);
                            string[] mas = newMessage.Split(' ');
                            await RegistrationAccountAsync(client, mas[0], mas[1], mas[2], mas[3], mas[4], mas[5], mas[6], mas[7], mas[8]);
                        }
                        else if (message.StartsWith("#CheckAccount"))
                        {
                            string[] CheckAcc = message.Split(' ');
                            Console.WriteLine($"На сервер пришел запрос на авторизацию пользователя.\n   Login: {CheckAcc[1]}\n   Password: {CheckAcc[2]}\n");

                            bool isAuthenticated = await CheckAccountInDatabaseAsync(CheckAcc[1], CheckAcc[2]);

                            await BroadcastMessageForCheckAccountAsync(isAuthenticated ? "1" : "0", client);
                            await SendUpdatedOnlineUsersListAsync();
                            RemoveDisconnectedClients();
                            return;
                        }
                        else if (message.StartsWith("#UpdateTcpEndPoint"))
                        {
                            string newMessage = message.Substring("UpdateTcpEndPoint ".Length);
                            string[] messageMassive = newMessage.Split(' ');
                            await UpdateTcpEndPointAsync(messageMassive[1], messageMassive[2], newTcpUsers);
                        }
                        else if (message.StartsWith("#LoadFromDBUsersInOnline"))
                        {
                            Console.WriteLine("Отправляем список пользователей со статусом онлайн");
                            await SendOnlineUsersListAsync(client);
                            await SendUpdatedOnlineUsersListAsync();
                        }
                        else if (message.StartsWith("#GetIdUserFromDB"))
                        {
                            Console.WriteLine("Отправка Id пользователя");
                            string editMes = message.Substring("#GetIdUserFromDB ".Length);
                            string[] masLoginAndPass = editMes.Split(' ');
                            await SendIdUserFromDBAsync(masLoginAndPass[0], masLoginAndPass[1], client);
                        }
                        else if (message.StartsWith("#GetSqlStringConnection_9221005"))
                        {
                            await GetSqlStringConnectionAsync(client);
                        }
                        else if (message.StartsWith("#SendMessageToUser"))
                        {
                            string readyMessage = message.Substring("#SendMessageToUser ".Length);
                            int indexOfMessage = readyMessage.IndexOf(';');
                            string messageFromUser = "";
                            string ReceiveUserId = "";
                            string SenderUserId = "";
                            string Id = "";
                            if (indexOfMessage > -1)
                            {
                                messageFromUser = readyMessage.Substring(0, indexOfMessage);
                                Id = readyMessage.Substring(indexOfMessage + 1);
                            }
                            string[] massId = Id.Split(' ');
                            ReceiveUserId = massId[0];
                            SenderUserId = massId[1];
                            await SendMessageToUserAsync(messageFromUser, ReceiveUserId, SenderUserId);
                        }
                        else if (message.StartsWith("#SavePhotoUsers"))
                        {
                            string readyMessage = message.Substring("#SavePhotoUsers ".Length);
                            string[] mas = readyMessage.Split(' ');
                            string photoBase64 = mas[2];
                            string login = mas[0];
                            string password = mas[1];
                            await SavePhotoUserAsync(login, password, photoBase64);
                        }
                        else if (message.StartsWith("#CloseConnection"))
                        {
                            string readyMessage = message.Substring("#CloseConnection ".Length);
                            await DisconnectClientAsync(readyMessage);
                            return;
                        }
                        else if (message.StartsWith("#GetFullMessageText"))
                        {
                            string messageReady = message.Substring("#GetFullMessageText ".Length);
                            string[] mass = messageReady.Split(' ');
                            await GetFullMessageTextAsync(mass[1], mass[0], mass[2]);
                        }
                        else
                        {
                            Console.WriteLine($"Получено сообщение: {message}");
                            await BroadcastMessageAsync(message, client);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке клиента: {ex.Message}");
            }
        }
        private async Task GetSqlStringConnectionAsync(TcpClient client)
        {
            try
            {
                string responseMessage = $@"#ConnectionString {ConnectionString}";
                byte[] buffer = Encoding.UTF8.GetBytes(responseMessage);
                NetworkStream stream = client.GetStream();

                byte[] sizeBuffer = BitConverter.GetBytes(buffer.Length);
                await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);

                await stream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке ответа проверки аккаунта: {ex.Message}");
            }
        }
        // регистрация аккаунта 
        private async Task SavePhotoUserAsync(string login, string password, string photoBase64)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(@"
            UPDATE Users 
            SET Photo = @Photo 
            WHERE Login = @Login AND Password = @Password", connection))
                {
                    byte[] photoData = Convert.FromBase64String(photoBase64);
                    command.Parameters.AddWithValue("@Photo", photoData);
                    command.Parameters.AddWithValue("@Login", login);
                    command.Parameters.AddWithValue("@Password", password);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        private async Task RegistrationAccountAsync(TcpClient client, string name, string surname, string patron, string login, string password, string databith, string phone, string email, string gender, string photo = "")
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Проверка на наличие такого логина (пользователя)
                    using (var command = new SqlCommand("SELECT COUNT(1) FROM Users WHERE Login = @login", connection))
                    {
                        command.Parameters.AddWithValue("@login", login);
                        int userCount = (int)await command.ExecuteScalarAsync();
                        if (userCount > 0)
                        {
                            Console.WriteLine("Аккаунт с таким логином уже существует.");
                            await SendResponseAsync(client, "#completereg 2");
                            return;
                        }
                    }

                    // Вставка нового пользователя в базу данных
                    using (var command = new SqlCommand(@"
                INSERT INTO Users (Name, Surname, Patronymic, Login, Password, DateOfBirth, PhoneNumber, Email, Gender)
                VALUES (@Name, @Surname, @Patronymic, @Login, @Password, @DateOfBirth, @PhoneNumber, @Email, @Gender)", connection))
                    {
                        command.Parameters.AddWithValue("@Name", name);
                        command.Parameters.AddWithValue("@Surname", surname);
                        command.Parameters.AddWithValue("@Patronymic", patron);
                        command.Parameters.AddWithValue("@Login", login);
                        command.Parameters.AddWithValue("@Password", password);
                        command.Parameters.AddWithValue("@DateOfBirth", databith);
                        command.Parameters.AddWithValue("@PhoneNumber", phone);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@Gender", gender);

                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        if (rowsAffected > 0)
                        {
                            Console.WriteLine("Новый аккаунт успешно зарегистрирован.");
                            await SendResponseAsync(client, "#completereg 1");
                        }
                        else
                        {
                            Console.WriteLine("Ошибка при регистрации нового аккаунта.");
                            await SendResponseAsync(client, "#completereg 0");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при регистрации аккаунта: {ex.Message}");
                await SendResponseAsync(client, "#completereg 0");
            }
        }

        // Вспомогательный метод для отправки ответа клиенту
        private async Task SendResponseAsync(TcpClient client, string responseMessage)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseMessage);
                NetworkStream stream = client.GetStream();

                byte[] sizeBuffer = BitConverter.GetBytes(buffer.Length);
                await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);
                await stream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке ответа клиенту: {ex.Message}");
            }
        }
        // получение истории сообщений пользователей
        private async Task GetFullMessageTextAsync(string senderId, string receiverId, string userConnection)
        {
            List<string> messages = new List<string>();

            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(@"
    SELECT MessageText 
    FROM Messages 
    WHERE (Id_User_Sender = @SenderId AND Id_User_Receiver = @ReceiverId) 
       OR (Id_User_Sender = @ReceiverId AND Id_User_Receiver = @SenderId)
    ORDER BY SentTime ASC", connection))
                {
                    command.Parameters.AddWithValue("@SenderId", senderId);
                    command.Parameters.AddWithValue("@ReceiverId", receiverId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string messageText = reader.GetString(0);
                            messages.Add(messageText);
                        }
                    }
                }
            }

            string fullMessagesHistory = string.Join("#message#", messages);
            byte[] buffer = Encoding.UTF8.GetBytes("#GetFullMessageText " + fullMessagesHistory);

            TcpClient targetClient = null;
            lock (clientLock)
            {
                foreach (TcpClient client in clients.ToArray())
                {
                    if (client.Connected && ((IPEndPoint)client.Client.RemoteEndPoint).ToString() == userConnection)
                    {
                        targetClient = client;
                        break;
                    }
                }
            }

            if (targetClient != null)
            {
                try
                {
                    Console.WriteLine("История отправлена пользователю...");
                    NetworkStream stream = targetClient.GetStream();

                    byte[] sizeBuffer = BitConverter.GetBytes(buffer.Length);
                    await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);
                    await stream.WriteAsync(buffer, 0, buffer.Length);
                    Console.WriteLine("История успешно отправлена");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при отправке сообщения пользователю {targetClient.Client.RemoteEndPoint}: {ex.Message}");
                }
            }
        }
        // отправка сообщения пользователю
        private async Task SendMessageToUserAsync(string message, string receiveUserId, string senderUserId)
        {
            string userConnection = "";
            string userName = "";
            string userSurname = "";

            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    using (var sqlGetEndPointCommand = new SqlCommand($@"SELECT Connection, Name, Surname FROM Users WHERE Id_User = @Id", connection))
                    {
                        sqlGetEndPointCommand.Parameters.AddWithValue("@Id", receiveUserId);

                        using (var reader = await sqlGetEndPointCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Получаем значения каждого столбца
                                userConnection = reader["Connection"].ToString();
                                userName = reader["Name"].ToString();
                                userSurname = reader["Surname"].ToString();
                            }
                            else
                            {
                                Console.WriteLine("Ошибка при поиске пользователя по Id");
                            }
                        }
                    }
                }

                byte[] buffer = Encoding.UTF8.GetBytes("#SendFromUser " + message);
                byte[] bufferSender = Encoding.UTF8.GetBytes("#GetUserSender " + senderUserId);

                await SendDataToUserAsync(userConnection, bufferSender);

                // Отправка сообщения
                await SendDataToUserAsync(userConnection, buffer);

                int countMessage = await GetUnreadMessagesCountAsync(senderUserId, receiveUserId);

                string userNameRes = "";
                string userSurnameRes = "";

                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    using (var sqlGetEndPointCommand = new SqlCommand($@"SELECT Name, Surname FROM Users WHERE Id_User = @Id", connection))
                    {
                        sqlGetEndPointCommand.Parameters.AddWithValue("@Id", senderUserId);

                        using (var reader = await sqlGetEndPointCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                // Получаем значения каждого столбца
                                userNameRes = reader["Name"].ToString();
                                userSurnameRes = reader["Surname"].ToString();
                            }
                            else
                            {
                                Console.WriteLine("Ошибка при поиске пользователя по Id");
                            }
                        }
                    }
                }

                byte[] bufferName = Encoding.UTF8.GetBytes("#NameAndSurname " + userSurnameRes + " " + userNameRes + " " + receiveUserId + " " + senderUserId);

                // Отправка информации об отправителе
                await SendDataToUserAsync(userConnection, bufferName);

                // Сохранение сообщения в базе данных
                await SaveMessageAsync(senderUserId, receiveUserId, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке сообщения: {ex.Message}");
            }
        }
        private async Task SendDataToUserAsync(string userConnection, byte[] data)
        {
            TcpClient targetClient = null;

            lock (clientLock)
            {
                foreach (TcpClient client in clients.ToArray())
                {
                    if (client.Connected && ((IPEndPoint)client.Client.RemoteEndPoint).ToString() == userConnection)
                    {
                        targetClient = client;
                        break;
                    }
                }
            }

            if (targetClient != null)
            {
                try
                {
                    Console.WriteLine("Отправка данных...");
                    NetworkStream stream = targetClient.GetStream();

                    // Отправка размера сообщения
                    byte[] sizeBuffer = BitConverter.GetBytes(data.Length);
                    await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);

                    // Отправка самого сообщения
                    await stream.WriteAsync(data, 0, data.Length);
                    Console.WriteLine("Данные успешно отправлены");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при отправке данных пользователю {targetClient.Client.RemoteEndPoint}: {ex.Message}");
                }
            }
        }
        private async Task<int> GetUnreadMessagesCountAsync(string senderUserId, string receiveUserId)
        {
            int countMessage = 0;

            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand($@"select * from MessagesNotReader where Id_User = @id and Id_Receiver = @id_receiver", connection))
                    {
                        command.Parameters.AddWithValue("@id", senderUserId);
                        command.Parameters.AddWithValue("@id_receiver", receiveUserId);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                countMessage = (int)reader["Count"];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении количества непрочитанных сообщений: {ex.Message}");
            }

            return countMessage;
        }
        private async Task SaveMessageAsync(string senderUserId, string receiveUserId, string message)
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqlCommand($@"INSERT INTO Messages (Id_User_Sender, Id_User_Receiver, MessageText, SentTime)
                                   VALUES (@Id_User_Sender, @Id_User_Receiver, @MessageText, GETDATE())", connection))
                    {
                        // Добавляем параметры
                        command.Parameters.AddWithValue("@Id_User_Sender", senderUserId);
                        command.Parameters.AddWithValue("@Id_User_Receiver", receiveUserId);
                        command.Parameters.AddWithValue("@MessageText", message);

                        // Выполняем команду
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сохранении сообщения: {ex.Message}");
            }
        }
        // отравка данных о текущем (т.е. тот кто авторизовался) пользователе
        private async Task SendIdUserFromDBAsync(string login, string password, TcpClient client)
        {
            string userData = "";
            string idUser = "";
            string tcpConnection = "";
            string name = "";
            string surname = "";
            string patronymic = "";
            DateTime dateOfBirth;
            string phoneNumber = "";
            string email = "";
            string gender = "";
            int status;

            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    using (var getIdCommand = new SqlCommand($@"SELECT Id_User, Name, Surname, Patronymic, DateOfBirth, PhoneNumber, Email, Gender, Status, Connection
FROM users WHERE Login = @Login AND Password = @Password", connection))
                    {
                        getIdCommand.Parameters.AddWithValue("@Login", login);
                        getIdCommand.Parameters.AddWithValue("@Password", password);

                        using (var reader = await getIdCommand.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                dateOfBirth = reader.GetDateTime(reader.GetOrdinal("DateOfBirth"));
                                string formattedDateOfBirth = dateOfBirth.ToString("dd.MM.yyyy");

                                // Получаем значения из результата запроса
                                idUser = reader["Id_User"].ToString();
                                tcpConnection = reader["Connection"].ToString();
                                name = reader["Name"].ToString();
                                surname = reader["Surname"].ToString();
                                patronymic = reader["Patronymic"].ToString();
                                phoneNumber = reader["PhoneNumber"].ToString();
                                email = reader["Email"].ToString();
                                gender = reader["Gender"].ToString();
                                status = (int)reader["Status"];

                                userData = $"{idUser} {tcpConnection} {login} {password} {name} {surname} {patronymic} {formattedDateOfBirth} {phoneNumber} {email} {gender} {status}";
                            }
                        }
                    }
                }

                // Отправка сообщения клиенту
                await SendUserDataToClientAsync(userData, client);
            }
            catch (Exception ex)
            {
                // Обработка ошибок
                Console.WriteLine("Ошибка при получении данных из базы данных: " + ex.Message);
            }
        }
        private async Task SendUserDataToClientAsync(string userData, TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] messageBytes = Encoding.UTF8.GetBytes("#GetIdUserFromDB " + userData);
                byte[] sizeBytes = BitConverter.GetBytes(messageBytes.Length);

                await stream.WriteAsync(sizeBytes, 0, sizeBytes.Length);
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await stream.FlushAsync();
                Console.WriteLine("Id отправлен и получен...");
            }
            catch (Exception ex)
            {
                // Обработка ошибок отправки сообщения
                Console.WriteLine("Ошибка при отправке ID пользователей: " + ex.Message);
            }
        }
        // Метод для проверки аккаунта в базе данных
        private async Task<bool> CheckAccountInDatabaseAsync(string login, string password)
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand($@"SELECT COUNT(*)
                                                    FROM users
                                                    WHERE Login = @Login AND Password = @Password", connection))
                    {
                        command.Parameters.AddWithValue("@Login", login);
                        command.Parameters.AddWithValue("@Password", password);

                        object result = await command.ExecuteScalarAsync();

                        if (result != null && result != DBNull.Value)
                        {
                            int count = Convert.ToInt32(result);
                            if (count > 0)
                            {
                                // Если логин и пароль успешно прошли проверку, обновляем поле "Status"
                                await UpdateUserStatusAsync(login, password);

                                // Обновляем поле Connection в таблице Users
                                await UpdateUserConnectionAsync(login, password, newTcpUsers);
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке аккаунта в базе данных: {ex.Message}");
                return false;
            }
        }
        private async Task UpdateUserStatusAsync(string login, string password)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();

                using (var updateCommand = new SqlCommand($@"UPDATE users
                                                    SET Status = 1
                                                    WHERE Login = @Login AND Password = @Password", connection))
                {
                    updateCommand.Parameters.AddWithValue("@Login", login);
                    updateCommand.Parameters.AddWithValue("@Password", password);
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
        }
        private async Task UpdateUserConnectionAsync(string login, string password, string newConnection)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();

                using (var updateCommand = new SqlCommand($@"UPDATE Users
                                                     SET Connection = @Connection
                                                     WHERE Login = @Login AND Password = @Password", connection))
                {
                    updateCommand.Parameters.AddWithValue("@Login", login);
                    updateCommand.Parameters.AddWithValue("@Password", password);
                    updateCommand.Parameters.AddWithValue("@Connection", newConnection);
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
        }
        private async Task UpdateTcpEndPointAsync(string login, string password, string tcpEndPoint)
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Обновляем поле Connection в таблице Users
                    using (var updateCommand = new SqlCommand($@"
                UPDATE Users
                SET Connection = @Connection
                WHERE Login = @Login AND Password = @Password", connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Login", login);
                        updateCommand.Parameters.AddWithValue("@Password", password);
                        updateCommand.Parameters.AddWithValue("@Connection", tcpEndPoint);
                        await updateCommand.ExecuteNonQueryAsync();
                    }
                }
                Console.WriteLine("Замена TcpEndPoint прошла успешно.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении TcpEndPoint: {ex.Message}");
            }
        }
        // метод отправляющий список пользователей, которые онлайн
        private async Task SendOnlineUsersListAsync(TcpClient client)
        {
            try
            {
                List<string> onlineUsers = new List<string>();

                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    using (var command = new SqlCommand("SELECT Id_User, Name, Surname, Connection FROM Users WHERE Status = 1", connection))
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            onlineUsers.Add($"{reader["Name"]} {reader["Surname"]}-{reader["Id_User"]};{reader["Connection"]}");
                        }
                    }
                }

                string userListMessage = string.Join(",", onlineUsers);

                NetworkStream stream = client.GetStream();
                byte[] messageBytes = Encoding.UTF8.GetBytes("#OnlineUsers " + userListMessage);
                byte[] sizeBytes = BitConverter.GetBytes(messageBytes.Length);

                await stream.WriteAsync(sizeBytes, 0, sizeBytes.Length);
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                await stream.FlushAsync();
                Console.WriteLine("Список отправлен...");
            }
            catch (Exception ex)
            {
                // Обработка ошибок отправки сообщения
                Console.WriteLine("Ошибка при отправке списка пользователей: " + ex.Message);
            }
        }
        private async Task BroadcastMessageForCheckAccountAsync(string message, TcpClient client)
        {
            try
            {
                string responseMessage = "#CheckAccount " + message; // Формируем сообщение в зависимости от результата проверки
                byte[] buffer = Encoding.UTF8.GetBytes(responseMessage);
                NetworkStream stream = client.GetStream();

                byte[] sizeBuffer = BitConverter.GetBytes(buffer.Length);
                await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);

                // Отправка самого сообщения
                await stream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке ответа проверки аккаунта: {ex.Message}");
            }
        }
        private async Task BroadcastMessageAsync(string message, TcpClient excludeClient)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);

            try
            {
                foreach (TcpClient client in clients.ToArray()) // Используем копию списка клиентов для безопасного перебора
                {
                    if (client != excludeClient && client.Connected)
                    {
                        Console.WriteLine("Сообщение от сервера ушло...");
                        NetworkStream stream = client.GetStream();

                        // Отправка размера сообщения
                        byte[] sizeBuffer = BitConverter.GetBytes(buffer.Length);
                        await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);

                        // Отправка самого сообщения
                        await stream.WriteAsync(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отправке сообщения клиенту: {ex.Message}");
            }
        }
        private async Task DisconnectClientAsync(string userTcpEndPoint)
        {
            try
            {
                List<TcpClient> clientsToRemove = new List<TcpClient>();

                TcpClient[] currentClients;
                lock (clientLock)
                {
                    currentClients = clients.ToArray();
                }

                foreach (TcpClient client in currentClients)
                {
                    if (client.Connected && ((IPEndPoint)client.Client.RemoteEndPoint).ToString() == userTcpEndPoint)
                    {
                        using (var connection = new SqlConnection(ConnectionString))
                        {
                            await connection.OpenAsync();
                            using (var sqlUpdateStatusCommand = new SqlCommand($@"UPDATE Users
SET Status = @Status, Connection = @Null
WHERE Connection = @Connection", connection))
                            {
                                sqlUpdateStatusCommand.Parameters.AddWithValue("@Status", 0);
                                sqlUpdateStatusCommand.Parameters.AddWithValue("@Connection", userTcpEndPoint.ToString()); // Условие для выбора строки
                                sqlUpdateStatusCommand.Parameters.AddWithValue("@Null", DBNull.Value); // Значение для обнуления поля Connection
                                await sqlUpdateStatusCommand.ExecuteNonQueryAsync();
                            }
                        }
                        Console.WriteLine($"Клиент {((IPEndPoint)client.Client.RemoteEndPoint).Address}:{((IPEndPoint)client.Client.RemoteEndPoint).Port} отключился.");
                        clientsToRemove.Add(client);
                    }
                }

                // Удаление отключенных клиентов из основного списка
                lock (clientLock)
                {
                    foreach (TcpClient clientToRemove in clientsToRemove)
                    {
                        clients.Remove(clientToRemove);
                        clientToRemove.Close();
                    }
                }

                Console.WriteLine("Обновление списка онлайн пользователей...");
                foreach (TcpClient client in currentClients)
                {
                    if (client.Connected)
                    {
                        await SendOnlineUsersListAsync(client);
                    }
                }
                Console.WriteLine("Список успешно обновлен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при отключении клиента: {ex.Message}");
            }
        }
        private async Task SendUpdatedOnlineUsersListAsync()
        {
            TcpClient[] currentClients;
            lock (clientLock)
            {
                currentClients = clients.ToArray();
            }

            foreach (TcpClient client in currentClients)
            {
                if (client.Connected)
                {
                    await SendOnlineUsersListAsync(client);
                }
            }
        }
    }
}