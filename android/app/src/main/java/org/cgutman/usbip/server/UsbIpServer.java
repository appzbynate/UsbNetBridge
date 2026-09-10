package org.cgutman.usbip.server;

import java.io.IOException;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

import org.cgutman.usbip.server.protocol.ProtoDefs;
import org.cgutman.usbip.server.protocol.cli.CommonPacket;
import org.cgutman.usbip.server.protocol.cli.DevListReply;
import org.cgutman.usbip.server.protocol.cli.ImportDeviceReply;
import org.cgutman.usbip.server.protocol.cli.ImportDeviceRequest;
import org.cgutman.usbip.server.protocol.dev.UsbIpDevicePacket;
import org.cgutman.usbip.server.protocol.dev.UsbIpSubmitUrb;
import org.cgutman.usbip.server.protocol.dev.UsbIpUnlinkUrb;

public class UsbIpServer {
	public static final int PORT = 3240;
	
	private UsbRequestHandler handler;
	private Thread serverThread;
	private ServerSocket serverSock;
	private ConcurrentHashMap<Socket, Thread> connections = new ConcurrentHashMap<>();

	public interface ClientEventListener {
		void onClientEvent(String message);
	}

	private volatile ClientEventListener eventListener;

	public void setEventListener(ClientEventListener listener) {
		this.eventListener = listener;
	}

	private void emit(String msg) {
		System.out.println(msg);
		ClientEventListener l = eventListener;
		if (l != null) {
			try {
				l.onClientEvent(msg);
			} catch (Exception ignored) {
			}
		}
	}
	
	// Returns true if a device is now attached on this socket
	private boolean handleRequest(Socket s) throws IOException {
		CommonPacket inMsg = CommonPacket.read(s.getInputStream());
		CommonPacket outMsg;
		
		if (inMsg == null) {
			s.close();
			return false;
		}
		
		boolean res = false;
		if (inMsg.code == ProtoDefs.OP_REQ_DEVLIST) {
			DevListReply dlReply = new DevListReply(inMsg.version);
			dlReply.devInfoList = handler.getDevices();
			if (dlReply.devInfoList == null) {
				dlReply.status = ProtoDefs.ST_NA;
			}
			outMsg = dlReply;
			emit("Client requested device list (" +
					(dlReply.devInfoList != null ? dlReply.devInfoList.size() : 0) + " devices)");
		}
		else if (inMsg.code == ProtoDefs.OP_REQ_IMPORT) {
			ImportDeviceRequest imReq = (ImportDeviceRequest)inMsg;
			ImportDeviceReply imReply = new ImportDeviceReply(inMsg.version);
			emit("Client importing busid=" + imReq.busid);
			
			res = handler.attachToDevice(s, imReq.busid);
			if (res) {
				imReply.devInfo = handler.getDeviceByBusId(imReq.busid);
				if (imReply.devInfo == null) {
					res = false;
				}
			}
			
			if (res) {
				imReply.status = ProtoDefs.ST_OK;
				emit("Import OK — now serving URBs for " + imReq.busid);
			}
			else {
				imReply.status = ProtoDefs.ST_NA;
				emit("Import FAILED for " + imReq.busid);
			}
			outMsg = imReply;
		}
		else {
			emit("Unknown USB/IP opcode: 0x" + Integer.toHexString(inMsg.code & 0xFFFF));
			return false;
		}
		
		s.getOutputStream().write(outMsg.serialize());
		s.getOutputStream().flush();
		return res;
	}
	
	private boolean handleDevRequest(Socket s) throws IOException {
		UsbIpDevicePacket inMsg = UsbIpDevicePacket.read(s.getInputStream());
		if (inMsg == null) {
			emit("Device channel: unknown/invalid URB command — ending session");
			return false;
		}
		
		if (inMsg.command == UsbIpDevicePacket.USBIP_CMD_SUBMIT) {
			handler.submitUrbRequest(s, (UsbIpSubmitUrb) inMsg);
			return true;
		}
		if (inMsg.command == UsbIpDevicePacket.USBIP_CMD_UNLINK) {
			handler.abortUrbRequest(s, (UsbIpUnlinkUrb) inMsg);
			return true;
		}

		emit("Device channel: unsupported command 0x" + Integer.toHexString(inMsg.command));
		return false;
	}
	
	public void killClient(Socket s) {
		Thread t = connections.remove(s);
		
		try {
			s.close();
		} catch (IOException e) {}
		
		if (t != null) {
			t.interrupt();
			try {
				t.join(2000);
			} catch (InterruptedException e) {}
		}
	}
	
	private void handleClient(final Socket s) {
		Thread t = new Thread(() -> {
			String peer = String.valueOf(s.getRemoteSocketAddress());
			emit("Client connected: " + peer);
			try {
				s.setTcpNoDelay(true);
				s.setKeepAlive(true);
				s.setSoTimeout(0);

				// USB/IP clients use one TCP connection for either:
				//  - DEVLIST (then usually disconnect), or
				//  - IMPORT + ongoing URB traffic until disconnect
				while (!Thread.currentThread().isInterrupted() && !s.isClosed()) {
					boolean imported = handleRequest(s);
					if (!imported) {
						// DEVLIST-only (or failed import). Client may send another
						// request on the same socket or close — loop and read again.
						continue;
					}

					// Imported: serve device URBs until the channel ends.
					try {
						while (!Thread.currentThread().isInterrupted() && handleDevRequest(s)) {
							// keep going
						}
					} catch (IOException e) {
						emit("URB channel closed (" + peer + "): " + e.getMessage());
					}
					// One import per connection (USB/IP normal). Exit client loop.
					break;
				}
			} catch (IOException e) {
				emit("Client disconnected (" + peer + "): " + e.getMessage());
			} catch (Exception e) {
				// Previously an NPE/etc. could kill the thread after a ~1s attach
				// and leave Windows showing a ghost "connected" device.
				emit("Client crashed (" + peer + "): " + e);
				e.printStackTrace();
			} finally {
				handler.cleanupSocket(s);
				connections.remove(s);
				try {
					s.close();
				} catch (IOException e) {}
				emit("Client cleanup done — sharing ended for " + peer);
			}
		}, "usbip-client");

		connections.put(s, t);
		t.start();
	}
	
	public void start(UsbRequestHandler handler) {
		this.handler = handler;
		
		Thread t = new Thread(() -> {
			try {
				serverSock = new ServerSocket();
				serverSock.setReuseAddress(true);
				serverSock.bind(new java.net.InetSocketAddress(PORT));
				emit("USB/IP listening on port " + PORT);
				while (!Thread.currentThread().isInterrupted()) {
					Socket client = serverSock.accept();
					try {
						client.setTcpNoDelay(true);
						client.setKeepAlive(true);
						client.setSoTimeout(0);
					} catch (IOException ignored) {}
					handleClient(client);
				}
			} catch (IOException e) {
				if (!Thread.currentThread().isInterrupted() && serverSock != null && !serverSock.isClosed()) {
					e.printStackTrace();
				}
			}
		}, "usbip-accept");
		
		serverThread = t;
		t.start();
	}
	
	public void stop() {
		if (serverSock != null) {
			try {
				serverSock.close();
			} catch (IOException e) {}
			
			serverSock = null;
		}
		
		if (serverThread != null) {
			serverThread.interrupt();
			
			try {
				serverThread.join(3000);
			} catch (InterruptedException e) {}
			
			serverThread = null;
		}
		
		for (Map.Entry<Socket, Thread> entry : connections.entrySet()) {
			try {
				entry.getKey().close();
			} catch (IOException e) {}
			
			entry.getValue().interrupt();
			
			try {
				entry.getValue().join(2000);
			} catch (InterruptedException e) {}
		}
		connections.clear();
	}
}
