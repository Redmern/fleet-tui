#!/usr/bin/env python3
"""Minimal fleetd control client: fleetctl.py SOCKET OP [json-fields]"""
import json
import socket
import struct
import sys


def send(sock, kind, obj):
    body = json.dumps(obj).encode()
    sock.sendall(struct.pack('<I', len(body) + 1) + bytes([kind]) + body)


def receive(sock):
    head = b''
    while len(head) < 5:
        head += sock.recv(5 - len(head))
    length = struct.unpack('<I', head[:4])[0] - 1
    body = b''
    while len(body) < length:
        body += sock.recv(length - len(body))
    return head[4], body


sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
sock.connect(sys.argv[1])
send(sock, 1, {'version': 1, 'role': 'control', 'os': 'test'})
receive(sock)
request = {'id': 1, 'op': sys.argv[2]}
if len(sys.argv) > 3:
    request.update(json.loads(sys.argv[3]))
send(sock, 30, request)
kind, body = receive(sock)
print(body.decode())
