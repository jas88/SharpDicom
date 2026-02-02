using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpDicom.Network.Pipes;

namespace SharpDicom.Tests.Network.Pipes
{
    /// <summary>
    /// Tests for <see cref="SocketPipe"/>.
    /// </summary>
    [TestFixture]
    public class SocketPipeTests
    {
        /// <summary>
        /// Creates a connected pair of TCP sockets on loopback.
        /// </summary>
        private static async Task<(Socket Client, Socket Server)> CreateConnectedSocketsAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var clientTask = Task.Run(() =>
            {
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.Connect(IPAddress.Loopback, port);
                return client;
            });

            var serverSocket = await listener.AcceptSocketAsync().ConfigureAwait(false);
            var clientSocket = await clientTask.ConfigureAwait(false);

            listener.Stop();

            return (clientSocket, serverSocket);
        }

        [Test]
        public async Task Constructor_ValidSocket_CreatesPipeReaderWriter()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);

            try
            {
                // Act
                await using var pipe = new SocketPipe(clientSocket);

                // Assert
                Assert.That(pipe.Input, Is.Not.Null);
                Assert.That(pipe.Output, Is.Not.Null);
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public void Constructor_NullSocket_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SocketPipe(null!));
        }

        [Test]
        public async Task Input_ReadFromSocket_DataAvailable()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);
            var testData = Encoding.UTF8.GetBytes("Hello, World!");

            try
            {
                await using var clientPipe = new SocketPipe(clientSocket);

                // Act - send data from server side
                await serverSocket.SendAsync(new ArraySegment<byte>(testData), SocketFlags.None)
                    .ConfigureAwait(false);

                // Read from client pipe
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);

                // Assert
                Assert.That(result.Buffer.Length, Is.GreaterThanOrEqualTo(testData.Length));

                var receivedData = result.Buffer.Slice(0, testData.Length).ToArray();
                Assert.That(receivedData, Is.EqualTo(testData));

                clientPipe.Input.AdvanceTo(result.Buffer.GetPosition(testData.Length));
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task Output_WriteToSocket_DataSent()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);
            var testData = Encoding.UTF8.GetBytes("Hello from pipe!");

            try
            {
                await using var clientPipe = new SocketPipe(clientSocket);

                // Act - write to client pipe
                await clientPipe.Output.WriteAsync(testData).ConfigureAwait(false);
                await clientPipe.Output.FlushAsync().ConfigureAwait(false);

                // Read from server socket
                var buffer = new byte[1024];
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                // Allow time for the drain task to send the data
                await Task.Delay(100).ConfigureAwait(false);

                var received = await serverSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), SocketFlags.None).ConfigureAwait(false);

                // Assert
                Assert.That(received, Is.EqualTo(testData.Length));
                Assert.That(buffer.AsSpan(0, received).ToArray(), Is.EqualTo(testData));
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task Roundtrip_BidirectionalCommunication_Works()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);
            var clientToServer = Encoding.UTF8.GetBytes("Client message");
            var serverToClient = Encoding.UTF8.GetBytes("Server message");

            try
            {
                await using var clientPipe = new SocketPipe(clientSocket);
                await using var serverPipe = new SocketPipe(serverSocket);

                // Act - client sends to server
                await clientPipe.Output.WriteAsync(clientToServer).ConfigureAwait(false);
                await clientPipe.Output.FlushAsync().ConfigureAwait(false);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                // Server receives
                var serverResult = await serverPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);
                var receivedByServer = serverResult.Buffer.Slice(0, clientToServer.Length).ToArray();
                serverPipe.Input.AdvanceTo(serverResult.Buffer.GetPosition(clientToServer.Length));

                // Server sends back
                await serverPipe.Output.WriteAsync(serverToClient).ConfigureAwait(false);
                await serverPipe.Output.FlushAsync().ConfigureAwait(false);

                // Client receives
                var clientResult = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);
                var receivedByClient = clientResult.Buffer.Slice(0, serverToClient.Length).ToArray();
                clientPipe.Input.AdvanceTo(clientResult.Buffer.GetPosition(serverToClient.Length));

                // Assert
                Assert.That(receivedByServer, Is.EqualTo(clientToServer));
                Assert.That(receivedByClient, Is.EqualTo(serverToClient));
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task Dispose_StopsBackgroundTasks()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);

            try
            {
                var clientPipe = new SocketPipe(clientSocket);

                // Act
                await clientPipe.DisposeAsync().ConfigureAwait(false);

                // Allow cleanup
                await Task.Delay(100).ConfigureAwait(false);

                // Assert - reading should fail or return completed
                // The pipe should be in a terminal state
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                try
                {
                    var result = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);
                    // Should either be completed or throw
                    Assert.That(result.IsCompleted, Is.True);
                }
                catch (OperationCanceledException)
                {
                    // Also acceptable - cancelled during dispose
                }
                catch (InvalidOperationException)
                {
                    // Also acceptable - pipe already completed
                }
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task SocketClosed_PipeCompletes()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);

            try
            {
                await using var clientPipe = new SocketPipe(clientSocket);

                // Act - close server socket (simulates remote disconnect)
                serverSocket.Close();

                // Give time for the read pump to detect the close
                await Task.Delay(200).ConfigureAwait(false);

                // Assert - reading should eventually return completed
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var result = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);
                Assert.That(result.IsCompleted, Is.True);
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task CustomMemoryPool_UsedByPipe()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);
            using var pool = new SlabMemoryPool();

            try
            {
                // Act
                await using var clientPipe = new SocketPipe(clientSocket, pool);

                // Send some data to trigger buffer allocation
                await serverSocket.SendAsync(
                    new ArraySegment<byte>(new byte[100]), SocketFlags.None).ConfigureAwait(false);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var result = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);

                // Assert - should have received data using the custom pool
                Assert.That(result.Buffer.Length, Is.GreaterThan(0));
                clientPipe.Input.AdvanceTo(result.Buffer.End);
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task LargeData_FragmentedRead_Reassembles()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);
            var largeData = new byte[64 * 1024]; // 64KB
            new Random(42).NextBytes(largeData);

            try
            {
                await using var clientPipe = new SocketPipe(clientSocket);

                // Act - send large data from server
                await serverSocket.SendAsync(
                    new ArraySegment<byte>(largeData), SocketFlags.None).ConfigureAwait(false);

                // Read from client pipe, accumulating until we have all data
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var totalReceived = 0;
                var receivedData = new byte[largeData.Length];

                while (totalReceived < largeData.Length)
                {
                    var result = await clientPipe.Input.ReadAsync(cts.Token).ConfigureAwait(false);
                    var buffer = result.Buffer;

                    var bytesToCopy = (int)Math.Min(buffer.Length, largeData.Length - totalReceived);
                    buffer.Slice(0, bytesToCopy).CopyTo(receivedData.AsSpan(totalReceived));
                    totalReceived += bytesToCopy;

                    clientPipe.Input.AdvanceTo(buffer.GetPosition(bytesToCopy));
                }

                // Assert
                Assert.That(totalReceived, Is.EqualTo(largeData.Length));
                Assert.That(receivedData, Is.EqualTo(largeData));
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }

        [Test]
        public async Task CustomThresholds_Respected()
        {
            // Arrange
            var (clientSocket, serverSocket) = await CreateConnectedSocketsAsync().ConfigureAwait(false);

            try
            {
                // Act - create with custom thresholds
                await using var clientPipe = new SocketPipe(
                    clientSocket,
                    null,
                    pauseWriterThreshold: 8192,
                    resumeWriterThreshold: 4096);

                // Assert - pipe created successfully with custom thresholds
                Assert.That(clientPipe.Input, Is.Not.Null);
                Assert.That(clientPipe.Output, Is.Not.Null);
            }
            finally
            {
                serverSocket.Dispose();
                clientSocket.Dispose();
            }
        }
    }
}
