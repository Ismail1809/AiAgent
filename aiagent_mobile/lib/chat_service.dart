import 'dart:convert';
import 'package:http/http.dart' as http;
import 'dart:async';

class ChatService {
  static const String _baseUrl = 'http://10.0.2.2:5090'; // Android emulator localhost

  static Future<List<Map<String, dynamic>>> fetchMessages(String chatCode) async {
    final url = Uri.parse('$_baseUrl/api/ChatApi/messages/$chatCode');
    final response = await http.get(url);
    if (response.statusCode == 200) {
      final decoded = jsonDecode(response.body);
      if (decoded is List) {
        return List<Map<String, dynamic>>.from(decoded);
      }
      throw Exception('Invalid messages response');
    } else {
      throw Exception('Failed to fetch messages: ${response.statusCode}');
    }
  }

  static Future<List<Map<String, dynamic>>> fetchUserChats(int userId) async {
    final url = Uri.parse('$_baseUrl/api/ChatApi/user-chats/$userId');
    final response = await http.get(url);
    if (response.statusCode == 200) {
      final decoded = jsonDecode(response.body);
      if (decoded is List) {
        return List<Map<String, dynamic>>.from(decoded);
      }
      throw Exception('Invalid user chats response');
    } else {
      throw Exception('Failed to fetch user chats: ${response.statusCode}');
    }
  }

  static Future<String> createNewChat(int userId) async {
    final url = Uri.parse('$_baseUrl/api/ChatApi/new-chat');
    final response = await http.post(
      url,
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(userId),
    );
    if (response.statusCode == 200) {
      final decoded = jsonDecode(response.body);
      return decoded['chatCode'] ?? '';
    } else {
      throw Exception('Failed to create new chat: ${response.statusCode}');
    }
  }

  static Future<String> createChat() async {
    final url = Uri.parse('$_baseUrl/api/ChatApi/create');
    final response = await http.post(url);
    if (response.statusCode == 200) {
      final decoded = jsonDecode(response.body);
      return decoded['chatCode'] ?? '';
    } else {
      throw Exception('Failed to create chat: ${response.statusCode}');
    }
  }

  static Future<String> sendMessage(String message, String chatCode) async {
    final url = Uri.parse('$_baseUrl/api/ChatApi/send');
    try {
      final response = await http.post(
        url,
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'message': message, 'chatCode': chatCode}),
      ).timeout(const Duration(seconds: 10));
      if (response.statusCode == 200) {
        try {
          final decoded = jsonDecode(response.body);
          // Try to extract choices[0].message.content
          if (decoded is Map && decoded.containsKey('choices')) {
            final choices = decoded['choices'];
            if (choices is List && choices.isNotEmpty) {
              final msg = choices[0]['message'];
              if (msg is Map && msg.containsKey('content')) {
                return msg['content'].toString();
              }
            }
          }
          return response.body;
        } catch (_) {
          return response.body;
        }
      } else {
        throw Exception('Failed to get response: ${response.statusCode}');
      }
    } on TimeoutException {
      return 'Request timed out. Please try again later.';
    }
  }

  static Future<String> getOAuthLink(String provider, int userId) async {
    String endpoint;
    if (provider.toLowerCase() == 'google') {
      endpoint = '/api/ChatApi/oauth-link/$provider/$userId';
    } else if (provider.toLowerCase() == 'outlook') {
      endpoint = '/api/ChatApi/oauth-link/$provider/$userId';
    } else {
      throw Exception('Unknown provider: $provider');
    }
    final url = Uri.parse('$_baseUrl$endpoint');
    final response = await http.get(url);
    if (response.statusCode == 200) {
      final decoded = jsonDecode(response.body);
      if (decoded is Map && decoded.containsKey('url')) {
        return decoded['url'];
      }
      throw Exception('Invalid OAuth link response');
    } else {
      throw Exception('Failed to get OAuth link: ${response.statusCode}');
    }
  }
} 