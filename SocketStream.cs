using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Commands
{
    public class SocketStream
    {
        private TcpClient _client = null;
        private NetworkStream _stream = null;
        private StreamWriter _sw = null;
        private IPEndPoint _IPEndPoint = null;
        private bool _isConnected = false;
        public SocketStream() { _client = new TcpClient(); }
        public bool Start(IPEndPoint iPEndPoint)
        {
            if (_isConnected == false)
            {
                _IPEndPoint = iPEndPoint;
                _client.Connect(_IPEndPoint);
                _stream = _client.GetStream();
                _sw = new StreamWriter(_stream);
                _isConnected = true;
                return true;
            }
            else { return false; }
        }
        public bool Stop()
        {
            if (_isConnected)
            {
                _sw.Flush();
                _stream = null;
                _sw = null;
                _client.Close();
                _isConnected = false;
                return true;
            }
            else { return false; }
        }
        public void Write(string s) { if (_isConnected) _sw.Write(s); }
        public void WriteLine(string s) { if (_isConnected) _sw.WriteLine(s); }
        ~SocketStream() { Stop(); }
    }
}