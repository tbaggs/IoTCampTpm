using System;
using System.Text;
using Windows.ApplicationModel.Background;
using Windows.System.Threading;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Devices.Tpm;
using System.Diagnostics;

namespace IoTCampTpm
{
    public sealed class StartupTask : IBackgroundTask
    {
        static BackgroundTaskDeferral deferral;
        static ThreadPoolTimer timer;
        static DeviceClient deviceClient;
        static string deviceId;
        static int sendFrequency = 5;


        ///**********************************************
        //    Placeholder: Add a Tpm Device object
        //***********************************************/
        static TpmDevice tpmDevice;


        ///**********************************************
        //    Placeholder: Add a Twin management object
        //***********************************************/
        static TwinCollection reportedProperties = new TwinCollection();


        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            deferral = taskInstance.GetDeferral();

            ///**********************************************
            //    Placeholder: Using the TPM chip in code
            //***********************************************/
            tpmDevice = new TpmDevice(0); // Use logical device 0 on the TPM
            string hubUri = tpmDevice.GetHostName();
            string sasToken = tpmDevice.GetSASToken();
            deviceId = tpmDevice.GetDeviceId();

            deviceClient = DeviceClient.Create(hubUri, AuthenticationMethodFactory.
                    CreateAuthenticationWithToken(deviceId, sasToken), TransportType.Amqp);


            ///**********************************************
            //    Placeholder: Register a Direct Method
            //***********************************************/
            deviceClient.SetMethodHandlerAsync("senddiagnostics", SendDiagnostics, null).Wait();


            ///**********************************************
            //    Placeholder: Initialize device twin properties
            //***********************************************/
            InitTelemetry();

            deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, null).Wait();


            timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromSeconds(sendFrequency));
        }


        ///**********************************************
        //    Placeholder: Timer_Tick Code
        //***********************************************/
        public static async void Timer_Tick(ThreadPoolTimer timer)
        {
            SendDeviceToCloudMessagesAsync();
            ReceiveC2dAsync();
        }


        /**********************************************
        Placeholder: SendDeviceToCloudMessageAsnyc
        ***********************************************/
        private static async void SendDeviceToCloudMessagesAsync()
        {
            double minTemperature = 20;
            double minHumidity = 60;
            Random rand = new Random();

            double currentTemperature = minTemperature + rand.NextDouble() * 15;
            double currentHumidity = minHumidity + rand.NextDouble() * 20;

            var telemetryDataPoint = new
            {
                deviceId = deviceId,
                temperature = currentTemperature,
                humidity = currentHumidity
            };

            string messageString = JsonConvert.SerializeObject(telemetryDataPoint);
            Message message = new Message(Encoding.ASCII.GetBytes(messageString));

            await deviceClient.SendEventAsync(message);

            Debug.WriteLine("{0} > Sent message: {1}", DateTime.Now, messageString);
        }

        /**********************************************
        Placeholder: Receive cloud to device message
        ***********************************************/
        private static async void ReceiveC2dAsync()
        {
            Message receivedMessage = await deviceClient.ReceiveAsync();

            if (receivedMessage != null)
            {
                Debug.WriteLine("{0} > Received message: {1}", DateTime.Now, Encoding.ASCII.GetString(receivedMessage.GetBytes()));
                await deviceClient.CompleteAsync(receivedMessage);
            }
        }

        ///**********************************************
        //    Placeholder: Direct Method callback
        //***********************************************/
        static Task<MethodResponse> SendDiagnostics(MethodRequest methodRequest, object userContext)
        {
            Debug.WriteLine("\t{0}", methodRequest.DataAsJson);
            Debug.WriteLine("\nReturning response for method {0}", methodRequest.Name);

            string result = "'Doing great here!!!'";
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
        }


        ///**********************************************
        //    Placeholder: Report initial telemetry settings
        //***********************************************/
        private static async void InitTelemetry()
        {
            Debug.WriteLine("Report initial telemetry config:");

            TwinCollection telemetryConfig = new TwinCollection();

            telemetryConfig["configId"] = "0";
            telemetryConfig["sendFrequency"] = "5";
            reportedProperties["telemetryConfig"] = telemetryConfig;

            Debug.WriteLine(JsonConvert.SerializeObject(reportedProperties));

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }


        ///**********************************************
        //    Placeholder: Callback for Desired Propert changes
        //***********************************************/
        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Debug.WriteLine("Desired property change:");
            Debug.WriteLine(JsonConvert.SerializeObject(desiredProperties));

            var currentTelemetryConfig = reportedProperties["telemetryConfig"];
            var desiredTelemetryConfig = desiredProperties["telemetryConfig"];

            if ((desiredTelemetryConfig != null) && (desiredTelemetryConfig["configId"] != currentTelemetryConfig["configId"]))
            {
                Debug.WriteLine("\nInitiating config change");

                currentTelemetryConfig["status"] = "Pending";
                currentTelemetryConfig["pendingConfig"] = desiredTelemetryConfig;

                await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);

                CompleteConfigChange();
            }
        }

        ///**********************************************
        //    Placeholder: Updates the actual properties
        //***********************************************/
        public static async void CompleteConfigChange()
        {
            var currentTelemetryConfig = reportedProperties["telemetryConfig"];

            Debug.WriteLine("\nCompleting config change");

            currentTelemetryConfig["configId"] = currentTelemetryConfig["pendingConfig"]["configId"];
            currentTelemetryConfig["sendFrequency"] = currentTelemetryConfig["pendingConfig"]["sendFrequency"];
            currentTelemetryConfig["status"] = "Success";
            currentTelemetryConfig["pendingConfig"] = null;

            sendFrequency = currentTelemetryConfig["sendFrequency"];
            timer.Cancel();

            timer = ThreadPoolTimer.CreatePeriodicTimer(Timer_Tick, TimeSpan.FromSeconds(sendFrequency));

            //TODO: Add a way to change the default ThreadPoolTimer...

            await deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
        }
    }
}
