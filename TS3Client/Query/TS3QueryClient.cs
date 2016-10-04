// TS3AudioBot - An advanced Musicbot for Teamspeak 3
// Copyright (C) 2016  TS3AudioBot contributors
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

namespace TS3Client.Query
{
	using Messages;
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Net.Sockets;

	public sealed class TS3QueryClient : TS3BaseClient
	{
		private readonly TcpClient tcpClient;
		private NetworkStream tcpStream;
		private StreamReader tcpReader;
		private StreamWriter tcpWriter;

		public TS3QueryClient(EventDispatchType dispatcher) : base(dispatcher)
		{
			tcpClient = new TcpClient();
		}

		protected override void ConnectInternal(ConnectionData conData)
		{
			try { tcpClient.Connect(conData.Hostname, conData.Port); }
			catch (SocketException ex) { throw new TS3CommandException(new CommandError(), ex); }

			tcpStream = tcpClient.GetStream();
			tcpReader = new StreamReader(tcpStream, Util.Encoder);
			tcpWriter = new StreamWriter(tcpStream, Util.Encoder) { NewLine = "\n" };

			for (int i = 0; i < 3; i++)
				tcpReader.ReadLine();
		}

		protected override void DisconnectInternal()
		{
			tcpWriter?.WriteLine("quit");
			tcpWriter?.Flush();
			tcpClient.Close();
		}

		protected override void NetworkLoop()
		{
			while (true)
			{
				string line;
				try { line = tcpReader.ReadLine(); }
				catch (IOException) { line = null; }
				if (line == null) break;
				if (string.IsNullOrWhiteSpace(line)) continue;

				var message = line.Trim();
				ProcessCommand(message);
			}
			Status = TS3ClientStatus.Disconnected;
		}

		protected override IEnumerable<IResponse> SendCommand(TS3Command com, Type targetType) // Synchronous
		{
			using (WaitBlock wb = new WaitBlock(targetType))
			{
				lock (LockObj)
				{
					requestQueue.Enqueue(wb);
					SendRaw(com.ToString());
				}

				return wb.WaitForMessage();
			}
		}

		private void SendRaw(string data)
		{
			tcpWriter.WriteLine(data);
			tcpWriter.Flush();
		}

		#region QUERY SPECIFIC COMMANDS

		public void Login(string username, string password)
			=> Send("login",
			new CommandParameter("client_login_name", username),
			new CommandParameter("client_login_password", password));
		public void UseServer(int svrId)
			=> Send("use",
			new CommandParameter("sid", svrId));

		#endregion

		public override void Dispose()
		{
			base.Dispose();

			lock (LockObj)
			{
				tcpWriter?.Dispose();
				tcpWriter = null;

				tcpReader?.Dispose();
				tcpReader = null;
			}
		}
	}
}