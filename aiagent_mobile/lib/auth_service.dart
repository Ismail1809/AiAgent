import 'dart:convert';
import 'dart:async';
import 'package:http/http.dart' as http;

class AuthService {
  static const String baseUrl = 'http://10.0.2.2:5090/api'; // For Android emulator
  // static const String baseUrl = 'http://localhost:5000/api'; // For iOS simulator

  static Future<Map<String, dynamic>> register({
    required String username,
    required String password,
    String? email,
  }) async {
    try {
      final response = await http
          .post(
            Uri.parse('$baseUrl/User/register'),
            headers: {'Content-Type': 'application/json'},
            body: json.encode({
              'username': username,
              'password': password,
              'email': email,
            }),
          )
          .timeout(const Duration(seconds: 8));

      if (response.statusCode == 200) {
        final data = json.decode(response.body);
        if (!data.containsKey('chatCode')) {
          throw Exception('No chatCode returned from server');
        }
        return data;
      } else {
        final error = json.decode(response.body);
        throw Exception(error['message'] ?? 'Registration failed');
      }
    } on TimeoutException {
      throw Exception('Request timed out. Please try again.');
    } catch (e) {
      throw Exception('Network error: $e');
    }
  }

  static Future<Map<String, dynamic>> login({
    required String username,
    required String password,
  }) async {
    try {
      final response = await http
          .post(
            Uri.parse('$baseUrl/User/login'),
            headers: {'Content-Type': 'application/json'},
            body: json.encode({
              'username': username,
              'password': password,
            }),
          )
          .timeout(const Duration(seconds: 8));

      if (response.statusCode == 200) {
        final data = json.decode(response.body);
        if (!data.containsKey('chatCode')) {
          throw Exception('No chatCode returned from server');
        }
        return data;
      } else {
        final error = json.decode(response.body);
        throw Exception(error['message'] ?? 'Login failed');
      }
    } on TimeoutException {
      throw Exception('Request timed out. Please try again.');
    } catch (e) {
      throw Exception('Network error: $e');
    }
  }

  static Future<Map<String, dynamic>> getProfile(int userId) async {
    final response = await http.get(
      Uri.parse('$baseUrl/User/profile/$userId'),
    );

    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to get profile: ${response.body}');
    }
  }

  static Future<void> unlinkGoogle(int userId) async {
    try {
      final response = await http.post(
        Uri.parse('$baseUrl/User/unlink/google/$userId'),
        headers: {'Content-Type': 'application/json'},
      );

      if (response.statusCode != 200) {
        throw Exception('Failed to unlink Google account: ${response.statusCode} - ${response.body}');
      }
    } catch (e) {
      throw Exception('Network error while unlinking Google account: $e');
    }
  }

  static Future<void> unlinkOutlook(int userId) async {
    try {
      final response = await http.post(
        Uri.parse('$baseUrl/User/unlink/outlook/$userId'),
        headers: {'Content-Type': 'application/json'},
      );

      if (response.statusCode != 200) {
        throw Exception('Failed to unlink Outlook account: ${response.statusCode} - ${response.body}');
      }
    } catch (e) {
      throw Exception('Network error while unlinking Outlook account: $e');
    }
  }

  static Future<void> deleteChat(int userId, String chatCode) async {
    try {
      final response = await http.delete(
        Uri.parse('$baseUrl/ChatApi/user/$userId/chat/$chatCode'),
        headers: {'Content-Type': 'application/json'},
      );

      if (response.statusCode != 200) {
        throw Exception('Failed to delete chat: ${response.statusCode} - ${response.body}');
      }
    } catch (e) {
      throw Exception('Network error while deleting chat: $e');
    }
  }
} 