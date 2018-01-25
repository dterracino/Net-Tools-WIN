﻿using System;
using System.Collections.Specialized;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Common
{
    public partial class MainForm : Form
    {
        delegate void ShowReceivedDataDelegate(byte[] data, int length);

        private ClientWebSocket wsClient;
        private byte[] buffer = new byte[100];
        private bool newMessage = true;
        NameValueCollection headers;
        private string proxyUrl;

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
        }

        private async void sendButton_Click(object sender, EventArgs e)
        {
            if (wsClient == null || wsClient.State != WebSocketState.Open)
            {
                await CreateWebSocketClient().ConfigureAwait(true);
                if (wsClient == null || wsClient.State != WebSocketState.Open)
                {
                    return;
                }
                await SendAsync().ConfigureAwait(true);
                await ReadCallback().ConfigureAwait(true);
            }
            else
            {
                await SendAsync().ConfigureAwait(true);
            }
        }

        private async Task SendAsync()
        {
            byte[] data = sendTextBox.Bytes;
            if (data.Length <= 0)
            {
                MessageBox.Show(this, "Nothing to send.", this.Text);
                return;
            }
            sendButton.Enabled = false;
            int tickCount = Environment.TickCount;
            try
            {
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;
                await wsClient.SendAsync(new ArraySegment<byte>(data, 0, data.Length),
                    sendTextBox.Binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                    true, token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text);
                sendButton.Enabled = true;
                return;
            }
            tickCount = Environment.TickCount - tickCount;
            status.Text = String.Format("Sent {0} byte(s) in {1} milliseconds",
                data.Length, tickCount);
            sendButton.Enabled = true;
        }

        private void ShowReceivedData(byte[] data, int length, bool lastMessage)
        {
            if (newMessage)
            {
                outputText.AppendText(string.Format("Message Received on {0}:{1}", 
                    DateTime.Now, Environment.NewLine));
            }

            outputText.Append(data, length);

            if (lastMessage)
            {
                outputText.AppendText(Environment.NewLine);
                outputText.AppendText(Environment.NewLine);
            }

            newMessage = lastMessage;
        }

        private async Task CreateWebSocketClient()
        {
            if (wsClient != null && wsClient.State == WebSocketState.Open)
            {
                return;
            }
            wsClient = new ClientWebSocket();
            if (headers != null)
            { 
                foreach(string name in headers)
                {
                    try
                    {
                        wsClient.Options.SetRequestHeader(name, headers.Get(name));
                    }
                    catch (ArgumentException ex)
                    {
                        MessageBox.Show(ex.Message, this.Text);
                        return;
                    }
                }
            }
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                wsClient.Options.Proxy = new WebProxy(proxyUrl);
            }
            wsClient.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            Uri uri;
            try
            {
                uri = new Uri(location.Text);
            }
            catch (UriFormatException ex)
            {
                MessageBox.Show(ex.Message, this.Text);
                return;
            }
            EnableDisable(false);
            try
            {
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;
                await wsClient.ConnectAsync(uri, token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, this.Text);
                EnableDisable(true);
            }
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await CloseWebSocketClient();
        }

        private async Task ReadCallback()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            while(true)
            {
                WebSocketReceiveResult result;
                try
                {
                     result = await wsClient
                        .ReceiveAsync(new ArraySegment<byte>(buffer),token)
                        .ConfigureAwait(true);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                if (result.Count > 0)
                {
                    ShowReceivedData(buffer, result.Count, result.EndOfMessage);
                }
                if (wsClient.State != WebSocketState.Open)
                {
                    await CloseWebSocketClient().ConfigureAwait(true);
                    if (wsClient.CloseStatus != WebSocketCloseStatus.NormalClosure)
                        MessageBox.Show(string.Format("WebSocket closed due to {0}.",
                            wsClient.CloseStatus), this.Text);
                    return;
                }
            }
        }

        private async void connect_Click(object sender, EventArgs e)
        {
            await CreateWebSocketClient().ConfigureAwait(true);
            await ReadCallback().ConfigureAwait(true);
        }

        private async void closeButton_Click(object sender, EventArgs e)
        {
            await CloseWebSocketClient().ConfigureAwait(true);
        }

        private async Task CloseWebSocketClient()
        {
            if (wsClient != null && wsClient.State == WebSocketState.Open)
            {
                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;
                await wsClient
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", token)
                    .ConfigureAwait(true);
                status.Text = "WebSocket closed.";
                EnableDisable(true);
            }
        }

        private void setHeaders_Click(object sender, EventArgs e)
        {
            NameValueCollection initialValues = new NameValueCollection();
            if (headers == null || headers.Count == 0)
            {
                initialValues.Add("Authorization", "Bearer token");
            }
            else
            {
                initialValues.Add(headers);
            }
            NameValueDialog headerForm = 
                new NameValueDialog("Request Headers", initialValues);
            headerForm.ShowDialog();
            headers = headerForm.NameValues;
        }

        private void proxyButton_Click(object sender, EventArgs e)
        {
            string defaultValue;
            if (string.IsNullOrEmpty(proxyUrl))
            {
                defaultValue = "http://localhost:8888";
            }
            else
            {
                defaultValue = proxyUrl;
            }
            DialogResult result = InputDialog.Show<Uri>(this, "HTTP Proxy",
                defaultValue, out Uri value);
            if (result == DialogResult.OK)
            {
                proxyUrl = value?.AbsoluteUri;
            }
        }

        private void EnableDisable(bool enable)
        {
            connect.Enabled = enable;
            setHeaders.Enabled = enable;
            proxyButton.Enabled = enable;
            location.ReadOnly = !enable;
        }
    }
}