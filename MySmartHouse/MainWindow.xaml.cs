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
using static MongoDB.Driver.WriteConcern;

namespace MySmartHouse
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource tokenSource;
        private bool isRunning = false;

        const string connectionUrl = "mongodb://localhost:27017";
        SerialPort ArduinoPort = new();
        SensorData sensorData;
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

        private void StartToggleButtonUpdateThread()
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

        private async void Connect_ClickAsync(object sender, RoutedEventArgs e)
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
                        var filter = Builders<BsonDocument>.Filter.Empty;
                        var projection = Builders<BsonDocument>.Projection.Include("actual.sensor_values.control").Exclude("_id");
                        var document = greenhouse.Find(filter).Project(projection).FirstOrDefault();

                        if (document != null)
                        {
                            control_toggleBtn.Content = document["actual"]["sensor_values"]["control"].AsInt32 == 1 ? "Авто" : "Ручное";
                            control_toggleBtn.IsChecked = document["actual"]["sensor_values"]["control"].AsInt32 == 1 ? true : false;
                        }

                        await UpdateToggleButtonStatesAsync();


                        onBtn.IsEnabled = true;
                        offBtn.IsEnabled = false;

                        conBtn.Content = "Отключиться";

                        string pn = portBox.SelectedValue.ToString();
                        ArduinoPort.PortName = pn;
                        if (ArduinoPort.IsOpen == false)
                        {
                            ArduinoPort.BaudRate = (9600);
                            ArduinoPort.DataReceived += ArduinoPort_DataReceived;
                            ArduinoPort.Open();
                        }

                        if (!isRunning)
                        {
                            isRunning = true;
                            tokenSource = new CancellationTokenSource();

                            // Запуск потока для обновления Combobox'ов
                            Thread updateThread = new Thread(UpdateComboBoxes);
                            updateThread.IsBackground = true;
                            updateThread.Start();
                            StartToggleButtonUpdateThread();
                            await Task.Run(() => ExecuteRepeatedlyAsync(tokenSource.Token));
                        }

                        break;
                    case 0:
                        onBtn.IsEnabled = false;
                        offBtn.IsEnabled = false;

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

        private async Task UpdateToggleButtonStatesAsync()
        {
            await Dispatcher.Invoke(async () =>
            {
                var filter = Builders<BsonDocument>.Filter.Empty;
                var projection = Builders<BsonDocument>.Projection.Include("actual.devices").Exclude("_id");
                var document = await greenhouse.Find(filter).Project(projection).FirstOrDefaultAsync();

                if (document != null)
                {
                    var deviceTags = new List<string> { "watering", "heating", "lighting", "humidification", "fan_general", "fan_circulation", "framuga", "co2", "curtain" };

                    foreach (var tag in deviceTags)
                    {
                        var isEnabled = document["actual"]["devices"][tag]["enabled"].AsBoolean;

                        ToggleButton toggleButton = FindName($"{tag}_toggleBtn") as ToggleButton;
                        if (toggleButton != null)
                        {
                            toggleButton.Content = isEnabled ? "Включено" : "Отключено";
                            toggleButton.IsChecked = isEnabled;

                            Ellipse circle = FindName($"{tag}_Circle") as Ellipse;
                            if (circle != null)
                            {
                                circle.Fill = isEnabled ? Brushes.Red : Brushes.Transparent;
                            }
                        }
                    }
                }
            });
        }

        private async Task ExecuteRepeatedlyAsync(CancellationToken cancellationToken)
        {
            while (sensorData == null && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("EXECUTEREPEATEDASYNC В РАБОТЕ!");
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

                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private void ArduinoPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
       {
            string data = ArduinoPort.ReadLine();

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

    private void OnButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ArduinoPort.WriteLine("on");
                Response = ArduinoPort.ReadLine();
                Console.WriteLine(Response);

                if (Response.Contains("o")) {
                    onBtn.IsEnabled = false;
                    offBtn.IsEnabled = true;
                    Console.WriteLine("Реле включено!");
                }
            }
            catch { }

        }

        private void OffButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ArduinoPort.WriteLine("off");
                string Response = ArduinoPort.ReadLine();
                Console.WriteLine(Response);

                if (Response.Contains("o"))
                {
                    onBtn.IsEnabled = true;
                    offBtn.IsEnabled = false;
                    Console.WriteLine("Реле выключено!");
                }
            }
            catch { }
        }

        private void InitializeComboBox()
        {
            for (int hours = 0; hours < 24; hours++)
            {
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

        private async Task UpdateDeviceStatusAsync(string deviceTag, bool isEnabled)
        {
            var filter = Builders<BsonDocument>.Filter.Empty;
            var update = Builders<BsonDocument>.Update.Set($"actual.devices.{deviceTag}.enabled", isEnabled);

            await greenhouse.UpdateOneAsync(filter, update);
        }

        private async void ToggleButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            ToggleButton toggleButton = sender as ToggleButton;
            if (toggleButton != null)
            {
                string tag = toggleButton.Tag as string;
                bool isEnabled = toggleButton.IsChecked == true;

                // Обновляем статус устройства в базе данных
                await UpdateDeviceStatusAsync(tag, isEnabled);

                string state = isEnabled ? "Включено" : "Отключено";

                Ellipse circle = FindName($"{tag}_Circle") as Ellipse;
                if (circle != null)
                {
                    circle.Fill = isEnabled ? Brushes.Red : Brushes.Transparent;
                }

                toggleButton.Content = state;
            }
        }

        private void Control_ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleButton toggleButton = sender as ToggleButton;
            if (toggleButton != null)
            {
                string state = toggleButton.IsChecked == true ? "Авто" : "Ручное";
                toggleButton.Content = state;

                var filter = Builders<BsonDocument>.Filter.Empty;
                var update = Builders<BsonDocument>.Update.Set("actual.sensor_values.control", toggleButton.IsChecked == true ? 1 : 0);

                greenhouse.UpdateMany(filter, update);
            }
        }

        private void History_Button_Click(object sender, RoutedEventArgs e)
        {
            History history = new History();
            history.Show();
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            if (checkBox != null)
            {
                bool state = (bool)checkBox.IsChecked;
                string tag = checkBox.Tag as string;
                Console.WriteLine("CHECKBOX: " + tag + ", STATE: " + (state ? "включено" : "выключено"));
            }
        }

        private async Task UpdateDeviceTimeAsync(string deviceName, string timeField, string newValue)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId("64dc5bc1c4b69d07df12306f"));
            var update = Builders<BsonDocument>.Update.Set($"actual.devices.{deviceName}.{timeField}", newValue);

            await greenhouse.UpdateOneAsync(filter, update);
        }

        private void UpdateComboBoxes()
        {
            while (true)
            {
                var devices = database.GetCollection<BsonDocument>("greenhouse")
                                      .Find(Builders<BsonDocument>.Filter.Empty)
                                      .FirstOrDefault()["actual"]["devices"].AsBsonDocument;

                foreach (var device in devices)
                {
                    string deviceName = device.Name;
                    var deviceData = device.Value.AsBsonDocument;
                    string timeMin = deviceData["time_min"].AsString;
                    string timeMax = deviceData["time_max"].AsString;

                    Dispatcher.Invoke(() =>
                    {
                        ComboBox minComboBox = FindName($"{deviceName}_min") as ComboBox;
                        ComboBox maxComboBox = FindName($"{deviceName}_max") as ComboBox;

                        if (minComboBox != null)
                            minComboBox.SelectedItem = timeMin;

                        if (maxComboBox != null)
                            maxComboBox.SelectedItem = timeMax;
                    });
                }

                Thread.Sleep(1000); // Пауза в 1 секунду
            }
        }

        private async void ComboBox_SelectionChangedAsync(object sender, SelectionChangedEventArgs e)
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
                    Console.WriteLine($"{deviceName}, {timeField} ({selectedValue})");
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
    }
}