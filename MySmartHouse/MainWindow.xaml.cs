using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Media;
using MySql.Data.MySqlClient;
using System.Data;

namespace MySmartHouse
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public CancellationTokenSource tokenSource;
        public bool isRunning = false;

        const string connectionUrl = "mongodb://localhost:27017";
        SerialPort ArduinoPort = new();
        SensorData sensorData;
        public readonly string connectionString = "Server=localhost;Database=greenhouse;Uid=root;Pwd=;";
        MongoClient client = new(connectionUrl);
        IMongoDatabase database;
        IMongoCollection<BsonDocument> greenhouse;

        string Response;
        int clickcount1;

        public MainWindow()
        {
            InitializeComponent();
            InitializeComboBox();

            database = client.GetDatabase("greenhouse");
            greenhouse = database.GetCollection<BsonDocument>("greenhouse");
        }

        public void StartToggleButtonUpdateThread()
        {
            Thread updateThread = new(() =>
            {
                while (true)
                {
                    UpdateToggleButtonStatesAsync().Wait(); // Ждем окончания асинхронной операции, не рекомендуется в продакшен-коде
                    Thread.Sleep(1000); // Приостанавливаем поток на 1 секунду
                }
            });

            updateThread.IsBackground = true; // Поток будет завершен, если основной поток завершится
            updateThread.Start();
        }

        public async void Connect_ClickAsync(object sender, RoutedEventArgs e)
        {
            try {
                if (!(clickcount1 == 1))
                {
                    clickcount1++;
                }
                else
                {
                    clickcount1 = 0;
                }

                switch (clickcount1)
                {
                    case 1:
                        try
                        {
                            string selectQuery = "SELECT control FROM sensor_values";

                            MySqlConnection connection;
                            MySqlCommand cmd;

                            connection = new MySqlConnection(connectionString);
                            cmd = new MySqlCommand();
                            cmd.Connection = connection;
                            cmd.CommandType = CommandType.Text;

                            try
                            {
                                connection.Open();

                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                            }

                            cmd.CommandText = selectQuery;

                            using (MySqlDataReader reader = cmd.ExecuteReader())
                            {
                                if (reader.HasRows)
                                {
                                    while (reader.Read())
                                    {
                                        int control = reader.GetInt32(0);

                                        control_toggleBtn.Content = control == 1 ? "Авто" : "Ручное";
                                        control_toggleBtn.IsChecked = control == 1 ? true : false;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Нет данных.");
                                }
                                reader.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при выполнении SELECT запроса: {ex.Message}");
                        }

                        await UpdateToggleButtonStatesAsync();

                        conBtn.Content = "Отключиться";

                        string pn = portBox.SelectedValue.ToString();
                        ArduinoPort.PortName = pn;
                        if (ArduinoPort.IsOpen == false)
                        {
                            ArduinoPort.BaudRate = (9600);
                            ArduinoPort.DataReceived += ArduinoPort_DataReceived;
                            ArduinoPort.Open();
                            Console.WriteLine(ArduinoPort.IsOpen);
                        }

                        if (!isRunning)
                        {
                            isRunning = true;
                            tokenSource = new CancellationTokenSource();

                            Thread updaterThread = new Thread(UpdateCheckBoxesFromDatabase);
                            updaterThread.Start();
                            // Запуск потока для обновления Combobox'ов
                            Thread updateThread = new Thread(UpdateComboBoxes);
                            updateThread.IsBackground = true;
                            updateThread.Start();
                            StartToggleButtonUpdateThread();
                            var errorUpdateThread = new Thread(UpdateErrorsList);
                            errorUpdateThread.IsBackground = true;
                            errorUpdateThread.Start();
                            await Task.Run(() => ExecuteRepeatedlyAsync(tokenSource.Token));
                        }

                        break;
                    case 0:

                        conBtn.Content = "Подключиться";

                        try
                        {
                            if (ArduinoPort.IsOpen == true)
                            {
                                ArduinoPort.Close();
                                if (isRunning)
                                {
                                    isRunning = false;
                                    tokenSource.Cancel();
                                }
                            }
                        }
                        catch
                        {

                        }

                        break;
                }
            } catch(Exception ex) {
                Console.WriteLine("Не удалось подключиться!");
                Console.WriteLine($"Ошибка: {ex}");
            }
        }

        public void UpdateErrorsList()
        {
            while (true)
            {
                Thread.Sleep(1000);

                string selectErrorsQuery = "SELECT * FROM errors";

                MySqlConnection connection;
                MySqlCommand cmd;

                connection = new MySqlConnection(connectionString);
                cmd = new MySqlCommand();
                cmd.Connection = connection;
                cmd.CommandType = CommandType.Text;

                try
                {
                    connection.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd.CommandText = selectErrorsQuery;

                try
                {
                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Errors_List.Items.Clear();
                                while (reader.Read())
                                {
                                    bool temperatureError = reader.GetBoolean("temperature");
                                    bool humidityError = reader.GetBoolean("humidity");
                                    bool uvError = reader.GetBoolean("uv");
                                    bool lightingError = reader.GetBoolean("lighting");
                                    bool co2Error = reader.GetBoolean("co2");
                                    bool heatingError = reader.GetBoolean("heating");

                                    if (temperatureError)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике температуры" });
                                        playErrorSound();
                                    }
                                    if (humidityError)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике влажности" });
                                        playErrorSound();
                                    }
                                    if (uvError)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике ультрафиолета" });
                                        playErrorSound();
                                    }
                                    if (lightingError)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике света" });
                                        playErrorSound();
                                    }
                                    if (co2Error)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике CO2" });
                                        playErrorSound();
                                    }
                                    if (heatingError)
                                    {
                                        Errors_List.Items.Add(new { Error = "Ошибка в датчике отопления" });
                                        playErrorSound();
                                    }
                                }
                            });
                        }
                        else
                        {
                            Console.WriteLine("Нет данных.");
                        }
                        reader.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении SELECT запроса: {ex.Message}");
                }
            }
        }
        
        public void playErrorSound()
        {
            string audioFilePath = @"C:\Users\NIK\source\repos\MySmartHouse\MySmartHouse\error.wav";

            SoundPlayer player = new SoundPlayer(audioFilePath);

            Console.WriteLine("Воспроизведение звука...");
            player.Play();
        }

        public async Task UpdateToggleButtonStatesAsync()
        {
            await Dispatcher.Invoke(async () =>
            {
                try
                {
                    string selectDevicesQuery = "SELECT * FROM devices";

                    MySqlConnection connection;
                    MySqlCommand cmd;

                    connection = new MySqlConnection(connectionString);
                    cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandType = CommandType.Text;

                    try
                    {
                        connection.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd.CommandText = selectDevicesQuery;

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                ToggleButton toggleButton = FindName("watering_toggleBtn") as ToggleButton;
                                Ellipse circle = FindName("watering_Circle") as Ellipse;

                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    switch (reader.GetName(i))
                                    {
                                        case "watering_enabled":
                                            bool wateringEnabled = reader.GetBoolean("watering_enabled");
                                            toggleButton = FindName("watering_toggleBtn") as ToggleButton;
                                            circle = FindName("watering_Circle") as Ellipse;

                                            toggleButton.Content = wateringEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = wateringEnabled;
                                            circle.Fill = wateringEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "heating_enabled":
                                            bool heatingEnabled = reader.GetBoolean("heating_enabled");
                                            toggleButton = FindName("heating_toggleBtn") as ToggleButton;
                                            circle = FindName("heating_Circle") as Ellipse;

                                            toggleButton.Content = heatingEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = heatingEnabled;
                                            circle.Fill = heatingEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "lighting_enabled":
                                            bool lightingEnabled = reader.GetBoolean("lighting_enabled");
                                            toggleButton = FindName("lighting_toggleBtn") as ToggleButton;
                                            circle = FindName("lighting_Circle") as Ellipse;

                                            toggleButton.Content = lightingEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = lightingEnabled;
                                            circle.Fill = lightingEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "humidification_enabled":
                                            bool humidificationEnabled = reader.GetBoolean("humidification_enabled");
                                            toggleButton = FindName("humidification_toggleBtn") as ToggleButton;
                                            circle = FindName("humidification_Circle") as Ellipse;

                                            toggleButton.Content = humidificationEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = humidificationEnabled;
                                            circle.Fill = humidificationEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "fan_general_enabled":
                                            bool fan_generalEnabled = reader.GetBoolean("fan_general_enabled");
                                            toggleButton = FindName("fan_general_toggleBtn") as ToggleButton;
                                            circle = FindName("fan_general_Circle") as Ellipse;

                                            toggleButton.Content = fan_generalEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = fan_generalEnabled;
                                            circle.Fill = fan_generalEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "fan_circulation_enabled":
                                            bool fan_circulationEnabled = reader.GetBoolean("fan_circulation_enabled");
                                            toggleButton = FindName("fan_circulation_toggleBtn") as ToggleButton;
                                            circle = FindName("fan_circulation_Circle") as Ellipse;

                                            toggleButton.Content = fan_circulationEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = fan_circulationEnabled;
                                            circle.Fill = fan_circulationEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "framuga_enabled":
                                            bool framugaEnabled = reader.GetBoolean("framuga_enabled");
                                            toggleButton = FindName("framuga_toggleBtn") as ToggleButton;
                                            circle = FindName("framuga_Circle") as Ellipse;

                                            toggleButton.Content = framugaEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = framugaEnabled;
                                            circle.Fill = framugaEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "co2_enabled":
                                            bool co2Enabled = reader.GetBoolean("co2_enabled");
                                            toggleButton = FindName("co2_toggleBtn") as ToggleButton;
                                            circle = FindName("co2_Circle") as Ellipse;

                                            toggleButton.Content = co2Enabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = co2Enabled;
                                            circle.Fill = co2Enabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                        case "curtain_enabled":
                                            bool curtainEnabled = reader.GetBoolean("curtain_enabled");
                                            toggleButton = FindName("curtain_toggleBtn") as ToggleButton;
                                            circle = FindName("curtain_Circle") as Ellipse;

                                            toggleButton.Content = curtainEnabled ? "Включено" : "Отключено";
                                            toggleButton.IsChecked = curtainEnabled;
                                            circle.Fill = curtainEnabled ? Brushes.Red : Brushes.Transparent;
                                            break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Нет данных в таблице devices.");
                        }
                        reader.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении SELECT запроса: {ex.Message}");
                }
            });
        }

        public async Task ExecuteRepeatedlyAsync(CancellationToken cancellationToken)
        {
/*            while (sensorData == null && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                double internalAirTemp = (int)Math.Round(sensorData.temp_internal_air);
                double outsideAirTemp = (int)Math.Round(sensorData.temp_outside_air);
                double groundTemp = (int)Math.Round(sensorData.temp_ground);
                double waterTemp = (int)Math.Round(sensorData.temp_water);
                double airHumidity = (int)Math.Round(sensorData.humidity_air);
                double groundHumidity = (int)Math.Round(sensorData.humidity_ground);
                int lighting = sensorData.lighting;
                int co2 = sensorData.co2;
                int uv = sensorData.uv;
                int rain = sensorData.rain;
                int wind = sensorData.wind;
                var timestamp = DateTime.UtcNow;

                string selectMaxQuery = "SELECT MAX(id) FROM devices;";
                int maxDevicesId = 0;
                MySqlConnection connection;
                MySqlCommand cmd;

                connection = new MySqlConnection(connectionString);
                cmd = new MySqlCommand();
                cmd.Connection = connection;
                cmd.CommandType = CommandType.Text;

                try
                {
                    connection.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd.CommandText = selectMaxQuery;

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                maxDevicesId = reader.GetInt32(i);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Нет данных в таблице devices.");
                    }
                    reader.Close();
                }

                string selectSensorQuery = "SELECT MAX(id) FROM sensor_values";
                int maxSensorId = 0;
                MySqlConnection connection2;
                MySqlCommand cmd2;

                connection2 = new MySqlConnection(connectionString);
                cmd2 = new MySqlCommand();
                cmd2.Connection = connection2;
                cmd2.CommandType = CommandType.Text;

                try
                {
                    connection.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd.CommandText = selectSensorQuery;

                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.HasRows)
                    {
                        while (reader.Read())
                        {
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                maxSensorId = reader.GetInt32(i);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Нет данных в таблице sensor_values.");
                    }
                    reader.Close();
                }

                try
                {
                    string updateQuery = $"INSERT history SET (sensor_values_id, devices_id, errors_id) = (@sensors_id, @devices_id, 1)";

                    MySqlConnection connection1;
                    MySqlCommand cmd1;

                    connection1 = new MySqlConnection(connectionString);
                    cmd1 = new MySqlCommand();
                    cmd1.Connection = connection;
                    cmd1.CommandType = CommandType.Text;

                    try
                    {
                        connection1.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd1.CommandText = updateQuery;

                    cmd1.Parameters.AddWithValue("@sensors_id", maxSensorId);
                    cmd1.Parameters.AddWithValue("@devices_id", maxDevicesId);

                    int rowsAffected = cmd1.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        Console.WriteLine("Данные успешно обновлены.");
                    }
                    else
                    {
                        Console.WriteLine("Данные не были обновлены.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
                }

                // Получаем текущее значение из базы данных
                var currentDocument = await greenhouse.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefaultAsync();

                if (currentDocument != null)
                {
                    var currentSensorValues = currentDocument["actual"]["sensor_values"].AsBsonDocument;

                    // Создаем новую запись для history
                    var newHistoryEntry = new BsonDocument
                    {
                        { "timestamp", timestamp },
                        { "sensor_values", new BsonDocument
                            {
                                { "control", 1 },
                                { "sensor_wind", wind },
                                { "sensor_rain", rain },
                                { "sensor_uv", uv },
                                { "temp_outside_air", outsideAirTemp },
                                { "temp_internal_air", internalAirTemp },
                                { "temp_ground", groundTemp },
                                { "temp_water", waterTemp },
                                { "humidity_ground", groundHumidity },
                                { "humidity_air", airHumidity },
                                { "co2", co2 },
                                { "lighting", lighting }
                            }
                        },
                        { "devices", new BsonDocument(currentDocument["actual"]["devices"].AsBsonDocument) } // Копируем actual.devices в history
                    };

                    var historyArray = currentDocument["history"].AsBsonArray;
                    historyArray.Add(newHistoryEntry);

                    // Обновляем запись в базе данных
                    var historyUpdate = Builders<BsonDocument>.Update.Set("history", historyArray);
                    await greenhouse.UpdateOneAsync(Builders<BsonDocument>.Filter.Empty, historyUpdate);

                    // Обновляем данные в actual.sensor_values
                    var actualUpdate = Builders<BsonDocument>.Update.Set("actual.sensor_values.temp_internal_air", internalAirTemp)
                        .Set("actual.sensor_values.temp_outside_air", outsideAirTemp)
                        .Set("actual.sensor_values.temp_ground", groundTemp)
                        .Set("actual.sensor_values.temp_water", waterTemp)
                        .Set("actual.sensor_values.humidity_air", airHumidity)
                        .Set("actual.sensor_values.humidity_ground", groundHumidity)
                        .Set("actual.sensor_values.lighting", lighting)
                        .Set("actual.sensor_values.co2", co2)
                        .Set("actual.sensor_values.sensor_uv", uv)
                        .Set("actual.sensor_values.sensor_rain", rain)
                        .Set("actual.sensor_values.sensor_wind", wind);

                    await greenhouse.UpdateOneAsync(Builders<BsonDocument>.Filter.Empty, actualUpdate);
                }
            }  

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);*/
    }

        public async Task UpdateErrorInDB(string error, bool isEnabled)
        {
            try
            {
                string updateQuery = $"UPDATE errors SET {error} = @isEnabled";

                MySqlConnection connection1;
                MySqlCommand cmd1;

                connection1 = new MySqlConnection(connectionString);
                cmd1 = new MySqlCommand();
                cmd1.Connection = connection1;
                cmd1.CommandType = CommandType.Text;

                try
                {
                    connection1.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd1.CommandText = updateQuery;

                cmd1.Parameters.AddWithValue("@isEnabled", isEnabled);

                int rowsAffected = cmd1.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    Console.WriteLine("Данные успешно обновлены.");
                }
                else
                {
                    Console.WriteLine("Данные не были обновлены.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
            }
        }

        public async void ArduinoPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
       {
            string data = "";
            try
            {
                data = ArduinoPort.ReadLine();

                Console.WriteLine(data);
            } catch { }
            

            try
            {
                sensorData = JsonSerializer.Deserialize<SensorData>(data);

                double internalAirTemp = (int)Math.Round(sensorData.temp_internal_air);
                double outsideAirTemp = (int)Math.Round(sensorData.temp_outside_air);
                double groundTemp = (int)Math.Round(sensorData.temp_ground);
                double waterTemp = (int)Math.Round(sensorData.temp_water);
                double airHumidity = (int)Math.Round(sensorData.humidity_air);
                double groundHumidity = (int)Math.Round(sensorData.humidity_ground);
                int lighting = sensorData.lighting;
                int co2 = sensorData.co2;
                int uv = sensorData.uv;
                int rain = sensorData.rain;
                int wind = sensorData.wind;

                // Ошибки
                bool errors_temperature = sensorData.errors_temperature;
                bool errors_humidity = sensorData.errors_humidity;
                bool errors_uv = sensorData.errors_uv;
                bool errors_lighting = sensorData.errors_lighting;
                bool errors_co2 = sensorData.errors_co2;
                bool errors_heating = sensorData.errors_heating;

                List<Task> updateTasks = new List<Task>();

        // Запускаем обновление в разных потоках
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("temperature", errors_temperature)));
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("humidity", errors_humidity)));
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("uv", errors_uv)));
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("lighting", errors_lighting)));
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("co2", errors_co2)));
        updateTasks.Add(Task.Run(async () => await UpdateErrorInDB("heating", errors_heating)));

        await Task.WhenAll(updateTasks);

                Dispatcher.Invoke(() =>
                {
                    temp_internal_air.Content = $"ТЕМПЕРАТУРА воздуха  С° {internalAirTemp}";
                    temp_ground_text.Content = $"ТЕМПЕРАТУРА почвы     С° {groundTemp}";
                    temp_water.Content = $"ТЕМПЕРАТУРА воды       С° {waterTemp}";
                    humidity_air.Content = $"ВЛАЖНОСТЬ воздуха     % {airHumidity}";
                    humidity_ground.Content = $"ВЛАЖНОСТЬ почвы        % {groundHumidity}";
                    lighting_text.Content = $"ОСВЕЩЕННОСТЬ             lx {lighting}";
                    co2_text.Content = $"CO2                                ppm {co2}";
                    temp_outside_air.Content = outsideAirTemp.ToString();
                    uv_text.Content = uv.ToString();
                    rain_text.Content = rain.ToString();
                    wind_text.Content = wind.ToString();
                });

            }
            catch (JsonException ex)
            {
                Console.WriteLine("Ошибка десериализации JSON: " + ex.Message);
            }
        }

        public void InitializeComboBox()
        {
            for (int hours = 0; hours < 24; hours++)
            {  // TODO: 00:00 == Выключено
                for (int minutes = 0; minutes < 60; minutes += 30)
                {
                    string time = $"{hours:D2}:{minutes:D2}";
                    watering_min.Items.Add(time);
                    watering_max.Items.Add(time);

                    heating_min.Items.Add(time);
                    heating_max.Items.Add(time);

                    lighting_min.Items.Add(time);
                    lighting_max.Items.Add(time);

                    humidification_min.Items.Add(time);
                    humidification_max.Items.Add(time);

                    fan_general_min.Items.Add(time);
                    fan_general_max.Items.Add(time);

                    fan_circulation_min.Items.Add(time);
                    fan_circulation_max.Items.Add(time);

                    framuga_min.Items.Add(time);
                    framuga_max.Items.Add(time);

                    co2_min.Items.Add(time);
                    co2_max.Items.Add(time);

                    curtain_min.Items.Add(time);
                    curtain_max.Items.Add(time);
                }
            }
        }

        public async Task UpdateDeviceStatusAsync(string deviceTag, bool isEnabled)
        {
            try
            {
                string updateQuery = $"UPDATE devices SET {deviceTag}_enabled = @isEnabled";

                MySqlConnection connection;
                MySqlCommand cmd;

                connection = new MySqlConnection(connectionString);
                cmd = new MySqlCommand();
                cmd.Connection = connection;
                cmd.CommandType = CommandType.Text;

                try
                {
                    connection.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd.CommandText = updateQuery;

                cmd.Parameters.AddWithValue("@isEnabled", isEnabled);

                int rowsAffected = cmd.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    Console.WriteLine("Данные успешно обновлены.");
                }
                else
                {
                    Console.WriteLine("Данные не были обновлены.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
            }
        }

        public async void ToggleButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            ToggleButton toggleButton = sender as ToggleButton;
            if (toggleButton != null)
            {
                string tag = toggleButton.Tag as string;
                bool isEnabled = toggleButton.IsChecked == true;

                // Обновляем статус устройства в базе данных
                await UpdateDeviceStatusAsync(tag, isEnabled);

                try
                {
                    char action = '0';

                    if (tag.Equals("watering") && isEnabled)
                    {
                        action = 'a';
                    } else if (tag.Equals("watering") && !isEnabled)
                    {
                        action = 'b';
                    } else if (tag.Equals("heating") && isEnabled)
                    {
                        action = 'c';
                    } else if (tag.Equals("heating") && !isEnabled)
                    {
                        action = 'd';
                    }
                    else if (tag.Equals("lighting") && isEnabled)
                    {
                        action = 'e';
                    }
                    else if (tag.Equals("lighting") && !isEnabled)
                    {
                        action = 'f';
                    }
                    else if (tag.Equals("humidification") && isEnabled)
                    {
                        action = 'g';
                    }
                    else if (tag.Equals("humidification") && !isEnabled)
                    {
                        action = 'h';
                    }
                    else if (tag.Equals("fan_general") && isEnabled)
                    {
                        action = 'i';
                    }
                    else if (tag.Equals("fan_general") && !isEnabled)
                    {
                        action = 'j';
                    }
                    else if (tag.Equals("fan_circulation") && isEnabled)
                    {
                        action = 'k';
                    }
                    else if (tag.Equals("fan_circulation") && !isEnabled)
                    {
                        action = 'l';
                    }
                    else if (tag.Equals("co2") && isEnabled)
                    {
                        action = 'm';
                    }
                    else if (tag.Equals("co2") && !isEnabled)
                    {
                        action = 'n';
                    }
                    else if (tag.Equals("framuga") && isEnabled)
                    {
                        action = 'o';
                    }
                    else if (tag.Equals("framuga") && !isEnabled)
                    {
                        action = 'p';
                    }

                    ArduinoPort.WriteLine(action.ToString());
                    Console.WriteLine(action);
                    string Response = ArduinoPort.ReadLine();
                    Console.WriteLine(Response);

                    if (Response.Contains("1"))
                    {
                        Console.WriteLine($"Реле было обновлено!");
                    }
                }
                catch { }

                string state = isEnabled ? "Включено" : "Отключено";

                Ellipse circle = FindName($"{tag}_Circle") as Ellipse;
                if (circle != null)
                {
                    circle.Fill = isEnabled ? Brushes.Red : Brushes.Transparent;
                }

                toggleButton.Content = state;
            }
        }

        public void Control_ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton toggleButton = sender as ToggleButton;
            if (toggleButton != null)
            {
                string state = toggleButton.IsChecked == true ? "Авто" : "Ручное";
                toggleButton.Content = state;

                try
                {
                    string updateQuery = $"UPDATE sensor_values SET control = @isAuto";

                    MySqlConnection connection;
                    MySqlCommand cmd;

                    connection = new MySqlConnection(connectionString);
                    cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandType = CommandType.Text;

                    try
                    {
                        connection.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd.CommandText = updateQuery;

                    cmd.Parameters.AddWithValue("@isAuto", toggleButton.IsChecked);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        Console.WriteLine("Данные успешно обновлены.");
                    }
                    else
                    {
                        Console.WriteLine("Данные не были обновлены.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
                }
            }
        }

        public void History_Button_Click(object sender, RoutedEventArgs e)
        {
            History history = new History();
            history.Show();
        }

        public void UpdateCheckBoxesFromDatabase()
        {
            while (true)
            {
                Thread.Sleep(1000);

                try {
                    string selectDevicesQuery = "SELECT watering_everyday, framuga_everyday, lighting_everyday, fan_general_everyday, fan_circulation_everyday, humidification_everyday, curtain_everyday, co2_everyday, heating_everyday FROM devices;";

                    MySqlConnection connection;
                    MySqlCommand cmd;

                    connection = new MySqlConnection(connectionString);
                    cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandType = CommandType.Text;

                    try
                    {
                        connection.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd.CommandText = selectDevicesQuery;

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    UpdateCheckBoxState(reader.GetName(i).Substring(0, reader.GetName(i).Length - "_everyday".Length), reader.GetBoolean(reader.GetName(i))); // В аргументы данного метода передаём название поля и получаем значение поля по его названию.
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Нет данных в таблице devices.");
                        }
                        reader.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении SELECT запроса: {ex.Message}");
                }
            }
        }

        public void UpdateCheckBoxState(string device, bool isEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                switch (device)
                {
                    case "watering":
                        watering_checkBox.IsChecked = isEnabled;
                        break;
                    case "heating":
                        heating_checkBox.IsChecked = isEnabled;
                        break;
                    case "lighting":
                        lighting_checkBox.IsChecked = isEnabled;
                        break;
                    case "humidification":
                        humidification_checkBox.IsChecked = isEnabled;
                        break;
                    case "fan_general":
                        fan_general_checkBox.IsChecked = isEnabled;
                        break;
                    case "fan_circulation":
                        fan_circulation_checkBox.IsChecked = isEnabled;
                        break;
                    case "framuga":
                        framuga_checkBox.IsChecked = isEnabled;
                        break;
                    case "co2":
                        co2_checkBox.IsChecked = isEnabled;
                        break;
                    case "curtain":
                        curtain_checkBox.IsChecked = isEnabled;
                        break;
                }
            });
        }

        public async void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                bool state = (bool)checkBox.IsChecked;
                string tag = checkBox.Tag as string;

                try
                {
                    string updateQuery = $"UPDATE devices SET {tag}_everyday = @isEveryday";

                    MySqlConnection connection;
                    MySqlCommand cmd;

                    connection = new MySqlConnection(connectionString);
                    cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandType = CommandType.Text;

                    try
                    {
                        connection.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd.CommandText = updateQuery;

                    cmd.Parameters.AddWithValue("@isEveryday", state);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    if (rowsAffected > 0)
                    {
                        Console.WriteLine("Данные успешно обновлены.");
                    }
                    else
                    {
                        Console.WriteLine("Данные не были обновлены.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
                }
            }
        }


        public async Task UpdateDeviceTimeAsync(string deviceName, string timeField, string newValue)
        {
            try
            {
                string updateQuery = $"UPDATE devices SET {deviceName}_{timeField} = @time";

                MySqlConnection connection;
                MySqlCommand cmd;

                connection = new MySqlConnection(connectionString);
                cmd = new MySqlCommand();
                cmd.Connection = connection;
                cmd.CommandType = CommandType.Text;

                try
                {
                    connection.Open();

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                }

                cmd.CommandText = updateQuery;

                cmd.Parameters.AddWithValue("@time", newValue);

                int rowsAffected = cmd.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    Console.WriteLine("Данные успешно обновлены.");
                }
                else
                {
                    Console.WriteLine("Данные не были обновлены.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при выполнении UPDATE запроса: {ex.Message}");
            }
        }

        public void UpdateComboBoxes()
        {
            while (true)
            {
                try
                {
                    string selectDevicesQuery = "SELECT watering_time_min, framuga_time_min, lighting_time_min, fan_general_time_min, fan_circulation_time_min, humidification_time_min, curtain_time_min, co2_time_min, heating_time_min, watering_time_max, framuga_time_max, lighting_time_max, fan_general_time_max, fan_circulation_time_max, humidification_time_max, curtain_time_max, co2_time_max, heating_time_max FROM devices;";

                    MySqlConnection connection;
                    MySqlCommand cmd;

                    connection = new MySqlConnection(connectionString);
                    cmd = new MySqlCommand();
                    cmd.Connection = connection;
                    cmd.CommandType = CommandType.Text;

                    try
                    {
                        connection.Open();

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка подключения к базе данных: {ex.Message}");
                    }

                    cmd.CommandText = selectDevicesQuery;

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.HasRows)
                        {
                            while (reader.Read())
                            {
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string deviceName = reader.GetName(i).Replace("_time_min", "").Replace("_time_max", "");
                                    var deviceData = reader.GetString(reader.GetName(i)).Substring(0, 5); // Оставляем только последние 6 символов, убирая секунды.

                                    Dispatcher.Invoke(() =>
                                    {
                                        bool isTimeMin = reader.GetName(i).EndsWith("_time_min");
                                        string comboBoxName = deviceName + (isTimeMin ? "_min" : "_max");

                                        ComboBox comboBox = FindName(comboBoxName) as ComboBox;

                                        if (comboBox != null)
                                            comboBox.SelectedItem = deviceData;
                                    });
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Нет данных в таблице devices.");
                        }
                        reader.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при выполнении SELECT запроса: {ex.Message}");
                }

                Thread.Sleep(1000);
            }
        }

        public async void ComboBox_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox != null)
            {
                string selectedValue = comboBox.SelectedItem as string;
                if (selectedValue != null)
                {
                    // Извлекаем имя устройства из имени ComboBox
                    string deviceName;
                    if (comboBox.Name.Split('_')[0] == "fan") deviceName = comboBox.Name.Split('_')[0] + '_' + comboBox.Name.Split('_')[1];
                    else deviceName = comboBox.Name.Split('_')[0];

                    // Определяем, является ли это min или max ComboBox
                    string actionType = comboBox.Name.EndsWith("min") ? "min" : "max";

                    string timeField = $"time_{actionType}";
                    await UpdateDeviceTimeAsync(deviceName, timeField, selectedValue);
                }
            }
        }

    }

    public class SensorData
    {
        public double temp_internal_air { get; set; }
        public double temp_outside_air { get; set; }
        public double temp_ground { get; set; }
        public double temp_water { get; set; }
        public double humidity_air { get; set; }
        public double humidity_ground { get; set; }
        public int lighting { get; set; }
        public int co2 { get; set; }
        public int uv { get; set; }
        public int rain { get; set; }
        public int wind { get; set; }
        public bool errors_temperature { get; set; }
        public bool errors_humidity { get; set; }
        public bool errors_uv { get; set; }
        public bool errors_lighting { get; set; }
        public bool errors_co2 { get; set; }
        public bool errors_heating { get; set; }
    }
}