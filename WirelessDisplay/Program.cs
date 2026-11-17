using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WirelessDisplay;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly ComboBox _modeComboBox = new();
    private readonly TextBox _ipTextBox = new();
    private readonly NumericUpDown _portNumeric = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly ListBox _logBox = new();
    private readonly Label _statusLabel = new();

    private CancellationTokenSource? _cts;
    private Task? _networkTask;
    private ReceiverWindow? _receiverWindow;
    private TcpListener? _listener;
    private TcpClient? _client;

    public MainForm()
    {
        Text = "Wireless Display";
        Width = 540;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = "Windows-to-Windows Wireless Display",
            Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 20)
        };

        var modeLabel = new Label { Text = "Mode:", AutoSize = true, Location = new Point(20, 70) };
        _modeComboBox.Items.AddRange(new object[] { "Sender", "Receiver" });
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeComboBox.Location = new Point(120, 66);
        _modeComboBox.Size = new Size(160, 24);

        var ipLabel = new Label { Text = "Target IP:", AutoSize = true, Location = new Point(20, 110) };
        _ipTextBox.Location = new Point(120, 106);
        _ipTextBox.Size = new Size(160, 24);
        _ipTextBox.Text = "192.168.1.10";

        var portLabel = new Label { Text = "Port:", AutoSize = true, Location = new Point(20, 150) };
        _portNumeric.Minimum = 1024;
        _portNumeric.Maximum = 65535;
        _portNumeric.Value = 5000;
        _portNumeric.Location = new Point(120, 146);
        _portNumeric.Size = new Size(160, 24);

        _startButton.Text = "Start";
        _startButton.Location = new Point(20, 195);
        _startButton.Size = new Size(110, 36);
        _startButton.Click += StartButton_Click;

        _stopButton.Text = "Stop";
        _stopButton.Location = new Point(170, 195);
        _stopButton.Size = new Size(110, 36);
        _stopButton.Click += StopButton_Click;

        _statusLabel.Text = "Ready";
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(20, 245);

        _logBox.Location = new Point(20, 280);
        _logBox.Size = new Size(470, 110);
        _logBox.Font = new Font(FontFamily.GenericMonospace, 9f);
        _logBox.HorizontalScrollbar = true;

        Controls.Add(title);
        Controls.Add(modeLabel);
        Controls.Add(_modeComboBox);
        Controls.Add(ipLabel);
        Controls.Add(_ipTextBox);
        Controls.Add(portLabel);
        Controls.Add(_portNumeric);
        Controls.Add(_startButton);
        Controls.Add(_stopButton);
        Controls.Add(_statusLabel);
        Controls.Add(_logBox);

        FormClosing += (_, _) => StopSession();
        Load += (_, _) => Log("App started. Choose Sender or Receiver mode.");
    }

    private async void StartButton_Click(object sender, EventArgs e)
    {
        if (_networkTask is not null && !_networkTask.IsCompleted)
        {
            Log("A session is already running.");
            return;
        }

        StopSession();

        _cts = new CancellationTokenSource();
        var mode = _modeComboBox.SelectedItem?.ToString();
        var port = (int)_portNumeric.Value;

        try
        {
            if (mode == "Sender")
            {
                var targetIp = _ipTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(targetIp))
                {
                    throw new InvalidOperationException("Target IP is required in Sender mode.");
                }

                _networkTask = Task.Run(() => RunSenderAsync(targetIp, port, _cts.Token));
                _statusLabel.Text = "Sender running";
                Log($"Connecting to {targetIp}:{port}...");
            }
            else
            {
                _networkTask = Task.Run(() => RunReceiverAsync(port, _cts.Token));
                _statusLabel.Text = "Receiver running";
                Log($"Waiting for sender on port {port}...");
            }
        }
        catch (Exception ex)
        {
            Log("Failed to start: " + ex.Message);
            _statusLabel.Text = "Error";
            StopSession();
        }
    }

    private void StopButton_Click(object sender, EventArgs e) => StopSession();

    private void StopSession()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _client?.Close();
        _receiverWindow?.Close();
        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _client = null;
        _receiverWindow = null;
        _statusLabel.Text = "Stopped";
        Log("Session stopped.");
    }

    private async Task RunSenderAsync(string targetIp, int port, CancellationToken token)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(targetIp, port).WaitAsync(token);
            _client = client;
            Log($"Connected to {targetIp}:{port}.");

            using var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                using var frame = CapturePrimaryScreen();
                using var payloadStream = new MemoryStream();
                frame.Save(payloadStream, ImageFormat.Jpeg);
                var payload = payloadStream.ToArray();

                var header = new byte[12];
                BitConverter.GetBytes(frame.Width).CopyTo(header, 0);
                BitConverter.GetBytes(frame.Height).CopyTo(header, 4);
                BitConverter.GetBytes(payload.Length).CopyTo(header, 8);

                var message = new byte[header.Length + payload.Length];
                Buffer.BlockCopy(header, 0, message, 0, header.Length);
                Buffer.BlockCopy(payload, 0, message, header.Length, payload.Length);

                await stream.WriteAsync(message, token);
                await Task.Delay(30, token);
            }
        }
        catch (OperationCanceledException)
        {
            Log("Sender loop cancelled.");
        }
        catch (Exception ex)
        {
            Log("Sender error: " + ex.Message);
        }
        finally
        {
            _statusLabel.Text = "Stopped";
        }
    }

    private async Task RunReceiverAsync(int port, CancellationToken token)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Log($"Listening on port {port}...");

            var client = await _listener.AcceptTcpClientAsync(token);
            _client = client;
            Log("Client connected.");

            _receiverWindow = new ReceiverWindow();
            _receiverWindow.Show();
            _receiverWindow.Activate();

            using var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                var header = await ReadExactlyAsync(stream, 12, token);
                if (header == null)
                {
                    break;
                }

                var width = BitConverter.ToInt32(header, 0);
                var height = BitConverter.ToInt32(header, 4);
                var payloadLength = BitConverter.ToInt32(header, 8);

                if (width <= 0 || height <= 0 || payloadLength <= 0 || payloadLength > 50_000_000)
                {
                    throw new InvalidOperationException("Invalid frame header received.");
                }

                var payload = await ReadExactlyAsync(stream, payloadLength, token);
                if (payload == null)
                {
                    break;
                }

                using var imageStream = new MemoryStream(payload);
                using var image = Image.FromStream(imageStream);
                using var bmp = new Bitmap(image);
                var clone = (Bitmap)bmp.Clone();
                _receiverWindow.UpdateFrame(clone);
            }
        }
        catch (OperationCanceledException)
        {
            Log("Receiver loop cancelled.");
        }
        catch (Exception ex)
        {
            Log("Receiver error: " + ex.Message);
        }
        finally
        {
            _statusLabel.Text = "Stopped";
            _receiverWindow?.Close();
        }
    }

    private static Bitmap CapturePrimaryScreen()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(screen.Bounds.X, screen.Bounds.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    private static async Task<byte[]?> ReadExactlyAsync(NetworkStream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length];
        var totalRead = 0;

        while (totalRead < length)
        {
            token.ThrowIfCancellationRequested();
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), token);
            if (bytesRead == 0)
            {
                return null;
            }

            totalRead += bytesRead;
        }

        return buffer;
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message)));
            return;
        }

        _logBox.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_logBox.Items.Count > 200)
        {
            _logBox.Items.RemoveAt(0);
        }

        _logBox.SelectedIndex = _logBox.Items.Count - 1;
        _logBox.TopIndex = _logBox.Items.Count - 1;
    }
}

public sealed class ReceiverWindow : Form
{
    private readonly PictureBox _pictureBox = new();

    public ReceiverWindow()
    {
        Text = "Wireless Display Receiver";
        WindowState = FormWindowState.Maximized;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;

        _pictureBox.Dock = DockStyle.Fill;
        _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        _pictureBox.BackColor = Color.Black;

        Controls.Add(_pictureBox);
    }

    public void UpdateFrame(Bitmap frame)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateFrame((Bitmap)frame.Clone())));
            return;
        }

        var clone = (Bitmap)frame.Clone();
        var previous = _pictureBox.Image;
        _pictureBox.Image = clone;
        previous?.Dispose();
    }
}

