import 'package:flutter/material.dart';
import 'package:flutter_markdown/flutter_markdown.dart';
import 'package:url_launcher/url_launcher.dart';
import 'chat_service.dart';
import 'auth_service.dart';

void main() {
  runApp(const AiAgentChatApp());
}

class AiAgentChatApp extends StatelessWidget {
  const AiAgentChatApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'AiAgent Chat',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple),
        useMaterial3: true,
      ),
      home: const AuthScreen(),
    );
  }
}

class AuthScreen extends StatefulWidget {
  const AuthScreen({super.key});

  @override
  State<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends State<AuthScreen> {
  bool _isLogin = true;
  final _formKey = GlobalKey<FormState>();
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();
  final _emailController = TextEditingController();
  bool _isLoading = false;
  String? _error;

  void _toggleMode() {
    setState(() {
      _isLogin = !_isLogin;
      _error = null;
      _formKey.currentState?.reset();
    });
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;

    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      Map<String, dynamic> result;
      
      if (_isLogin) {
        result = await AuthService.login(
          username: _usernameController.text.trim(),
          password: _passwordController.text,
        );
      } else {
        result = await AuthService.register(
          username: _usernameController.text.trim(),
          password: _passwordController.text,
          email: _emailController.text.trim().isEmpty ? null : _emailController.text.trim(),
        );
      }

      if (mounted) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute(
            builder: (_) => ChatScreen(
              userId: result['userId'],
              username: result['username'],
              chatCode: result['chatCode'],
            ),
          ),
        );
      }
    } catch (e) {
      setState(() {
        _error = e.toString();
      });
    } finally {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF181A20),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.symmetric(horizontal: 32),
          child: Form(
            key: _formKey,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Icon(Icons.lock, size: 64, color: Colors.white),
                const SizedBox(height: 24),
                Text(
                  _isLogin ? 'Welcome Back' : 'Create Account',
                  style: const TextStyle(
                    color: Colors.white,
                    fontWeight: FontWeight.bold,
                    fontSize: 24,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  _isLogin ? 'Sign in to continue' : 'Sign up to get started',
                  style: const TextStyle(
                    color: Colors.white70,
                    fontSize: 16,
                  ),
                ),
                const SizedBox(height: 32),
                TextFormField(
                  controller: _usernameController,
                  enabled: !_isLoading,
                  style: const TextStyle(color: Colors.white),
                  decoration: const InputDecoration(
                    hintText: 'Username',
                    hintStyle: TextStyle(color: Colors.white54),
                    filled: true,
                    fillColor: Color(0xFF262A34),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.all(Radius.circular(16)),
                      borderSide: BorderSide.none,
                    ),
                    contentPadding: EdgeInsets.symmetric(horizontal: 20, vertical: 16),
                  ),
                  validator: (value) {
                    if (value == null || value.trim().isEmpty) {
                      return 'Username is required';
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 16),
                if (!_isLogin) ...[
                  TextFormField(
                    controller: _emailController,
                    enabled: !_isLoading,
                    style: const TextStyle(color: Colors.white),
                    decoration: const InputDecoration(
                      hintText: 'Email (optional)',
                      hintStyle: TextStyle(color: Colors.white54),
                      filled: true,
                      fillColor: Color(0xFF262A34),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.all(Radius.circular(16)),
                        borderSide: BorderSide.none,
                      ),
                      contentPadding: EdgeInsets.symmetric(horizontal: 20, vertical: 16),
                    ),
                  ),
                  const SizedBox(height: 16),
                ],
                TextFormField(
                  controller: _passwordController,
                  enabled: !_isLoading,
                  obscureText: true,
                  style: const TextStyle(color: Colors.white),
                  decoration: const InputDecoration(
                    hintText: 'Password',
                    hintStyle: TextStyle(color: Colors.white54),
                    filled: true,
                    fillColor: Color(0xFF262A34),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.all(Radius.circular(16)),
                      borderSide: BorderSide.none,
                    ),
                    contentPadding: EdgeInsets.symmetric(horizontal: 20, vertical: 16),
                  ),
                  validator: (value) {
                    if (value == null || value.isEmpty) {
                      return 'Password is required';
                    }
                    if (!_isLogin && value.length < 6) {
                      return 'Password must be at least 6 characters';
                    }
                    return null;
                  },
                ),
                if (_error != null) ...[
                  const SizedBox(height: 16),
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: Colors.red.withOpacity(0.1),
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: Colors.red.withOpacity(0.3)),
                    ),
                    child: Text(
                      _error!,
                      style: const TextStyle(color: Colors.red),
                    ),
                  ),
                ],
                const SizedBox(height: 24),
                SizedBox(
                  width: double.infinity,
                  child: ElevatedButton(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: const Color(0xFF23272F), // dark, smooth grey
                      foregroundColor: Colors.white,
                      elevation: 4,
                      shadowColor: Colors.black45,
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(16),
                      ),
                    ),
                    onPressed: _isLoading ? null : _submit,
                    child: _isLoading
                        ? const SizedBox(
                            width: 24,
                            height: 24,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          )
                        : Text(_isLogin ? 'Sign In' : 'Sign Up', style: const TextStyle(fontSize: 16)),
                  ),
                ),
                const SizedBox(height: 16),
                TextButton(
                  onPressed: _isLoading ? null : _toggleMode,
                  style: TextButton.styleFrom(
                    foregroundColor: Colors.white,
                    backgroundColor: Colors.transparent,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(16),
                    ),
                    overlayColor: Colors.white24, // Fix: just the color, not MaterialStateProperty
                  ),
                  child: Text(
                    _isLogin ? 'Don\'t have an account? Sign Up' : 'Already have an account? Sign In',
                    style: const TextStyle(color: Colors.white),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class ChatScreen extends StatefulWidget {
  final int userId;
  final String username;
  final String chatCode;
  const ChatScreen({super.key, required this.userId, required this.username, required this.chatCode});

  @override
  State<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends State<ChatScreen> {
  final GlobalKey<ScaffoldState> _scaffoldKey = GlobalKey<ScaffoldState>();
  final List<_ChatMessage> _messages = [];
  bool _showHint = true;
  final TextEditingController _controller = TextEditingController();
  bool _isSending = false;
  String? _chatCode;
  Future<List<Map<String, dynamic>>> _fetchChatsFuture = Future.value([]);

  @override
  void initState() {
    super.initState();
    _chatCode = widget.chatCode;
    _loadMessages();
    _fetchChatsFuture = ChatService.fetchUserChats(widget.userId);
  }

  Future<void> _refreshChats() async {
    setState(() {
      _fetchChatsFuture = ChatService.fetchUserChats(widget.userId);
    });
  }

  Future<void> _loadMessages() async {
    if (_chatCode == null) return;
    try {
      final messages = await ChatService.fetchMessages(_chatCode!);
      setState(() {
        _messages.clear();
        _messages.addAll(messages.map((m) => _ChatMessage(
          text: m['content'] ?? '',
          isUser: (m['sender'] ?? '').toLowerCase() == 'user',
          time: DateTime.tryParse(m['createdAt'] ?? '') ?? DateTime.now(),
        )));
        _showHint = _messages.isEmpty;
      });
    } catch (e) {
      // Optionally show error
    }
  }

  void _sendMessage() async {
    final text = _controller.text.trim();
    if (text.isEmpty || _chatCode == null) return;
    setState(() {
      _showHint = false;
      _messages.add(_ChatMessage(text: text, isUser: true, time: DateTime.now()));
      _isSending = true;
      _controller.clear();
    });
    print('Sending message: $text');
    try {
      final aiResponse = await ChatService.sendMessage(text, _chatCode!);
      print('AI response: $aiResponse');
      setState(() {
        _messages.add(_ChatMessage(text: aiResponse, isUser: false, time: DateTime.now()));
        _isSending = false;
      });
    } catch (e) {
      print('Error sending message: $e');
      setState(() {
        _messages.add(_ChatMessage(text: 'Error: ${e.toString()}', isUser: false, time: DateTime.now()));
        _isSending = false;
      });
    }
  }

  Future<void> _createNewChat() async {
    try {
      final newChatCode = await ChatService.createNewChat(widget.userId);
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(
          builder: (_) => ChatScreen(
            userId: widget.userId,
            username: widget.username,
            chatCode: newChatCode,
          ),
        ),
      );
      await _refreshChats();
    } catch (e) {
      // Optionally show error
    }
  }

  void _showManageAccountModal() async {
    String email = '';
    bool isLoading = true;
    String? error;
    String? outlookStatus;
    String? googleStatus;
    final emailController = TextEditingController();
    final userId = widget.userId;
    final username = widget.username;
    String passwordMasked = '********';
    
    // Load initial status
    try {
      final profile = await AuthService.getProfile(userId);
      outlookStatus = (profile['outlookRefreshToken'] != null && profile['outlookRefreshToken'].toString().isNotEmpty)
        ? 'Linked' : 'Not linked';
      googleStatus = (profile['googleRefreshToken'] != null && profile['googleRefreshToken'].toString().isNotEmpty)
        ? 'Linked' : 'Not linked';
      email = profile['email'] ?? '';
      emailController.text = email;
    } catch (e) {
      error = e.toString();
    } finally {
      isLoading = false;
    }



    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.grey[900],
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
      ),
      builder: (context) {
        return Padding(
          padding: EdgeInsets.only(
            left: 24, right: 24,
            top: 24,
            bottom: MediaQuery.of(context).viewInsets.bottom + 24,
          ),
          child: StatefulBuilder(
            builder: (context, setModalState) {
              return isLoading
                  ? const Center(child: CircularProgressIndicator(color: Colors.white))
                  : Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Center(
                          child: CircleAvatar(
                            radius: 36,
                            backgroundColor: Colors.white,
                            child: Text(
                              username.isNotEmpty ? username[0].toUpperCase() : '?',
                              style: const TextStyle(fontSize: 32, color: Colors.black),
                            ),
                          ),
                        ),
                        const SizedBox(height: 16),
                        Center(
                          child: Text(
                            username,
                            style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 20, color: Colors.white),
                          ),
                        ),
                        const SizedBox(height: 16),
                        TextFormField(
                          controller: emailController,
                          style: const TextStyle(color: Colors.white),
                          decoration: const InputDecoration(
                            labelText: 'Email',
                            labelStyle: TextStyle(color: Colors.white70),
                            filled: true,
                            fillColor: Color(0xFF262A34),
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        ElevatedButton(
                          style: ElevatedButton.styleFrom(
                            backgroundColor: Colors.white,
                            foregroundColor: Colors.black,
                          ),
                          onPressed: () async {
                            setModalState(() => isLoading = true);
                            try {
                              // TODO: Call backend to update email
                              // await AuthService.updateEmail(userId, emailController.text.trim());
                              setModalState(() {
                                email = emailController.text.trim();
                                isLoading = false;
                              });
                            } catch (e) {
                              setModalState(() {
                                error = e.toString();
                                isLoading = false;
                              });
                            }
                          },
                          child: const Text('Save Email'),
                        ),
                        const SizedBox(height: 16),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Outlook:', style: TextStyle(fontWeight: FontWeight.w500, color: Colors.white)),
                            Row(
                              children: [
                                Icon(
                                  outlookStatus == 'Linked' ? Icons.check_circle : Icons.cancel,
                                  color: outlookStatus == 'Linked' ? Colors.green : Colors.red,
                                  size: 20,
                                ),
                                const SizedBox(width: 8),
                                Text(
                                  outlookStatus ?? 'Unknown', 
                                  style: TextStyle(
                                    color: outlookStatus == 'Linked' ? Colors.green : Colors.red,
                                    fontWeight: FontWeight.w500,
                                  ),
                                ),
                              ],
                            ),
                                                         if (outlookStatus != 'Linked')
                               ElevatedButton(
                                 style: ElevatedButton.styleFrom(
                                   backgroundColor: Colors.white,
                                   foregroundColor: Colors.black,
                                 ),
                                                                  onPressed: () async {
                                    try {
                                      final url = await ChatService.getOAuthLink('outlook', userId);
                                      // Close the modal first
                                      Navigator.of(context).pop();
                                      // Then open the OAuth link
                                      await launchUrl(
                                        Uri.parse(url),
                                        mode: LaunchMode.externalApplication,
                                      );
                                    } catch (e) {
                                      setModalState(() {
                                        error = e.toString();
                                      });
                                    }
                                  },
                                 child: const Text('Link'),
                               )
                             else
                               ElevatedButton(
                                 style: ElevatedButton.styleFrom(
                                   backgroundColor: Colors.red,
                                   foregroundColor: Colors.white,
                                 ),
                                                                   onPressed: () async {
                                    try {
                                      await AuthService.unlinkOutlook(userId);
                                      setModalState(() {
                                        outlookStatus = 'Not linked';
                                        error = null;
                                      });
                                    } catch (e) {
                                      setModalState(() {
                                        error = e.toString();
                                      });
                                    }
                                  },
                                 child: const Text('Unlink'),
                               ),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Google:', style: TextStyle(fontWeight: FontWeight.w500, color: Colors.white)),
                            Row(
                              children: [
                                Icon(
                                  googleStatus == 'Linked' ? Icons.check_circle : Icons.cancel,
                                  color: googleStatus == 'Linked' ? Colors.green : Colors.red,
                                  size: 20,
                                ),
                                const SizedBox(width: 8),
                                Text(
                                  googleStatus ?? 'Unknown', 
                                  style: TextStyle(
                                    color: googleStatus == 'Linked' ? Colors.green : Colors.red,
                                    fontWeight: FontWeight.w500,
                                  ),
                                ),
                              ],
                            ),
                                                         if (googleStatus != 'Linked')
                               ElevatedButton(
                                 style: ElevatedButton.styleFrom(
                                   backgroundColor: Colors.white,
                                   foregroundColor: Colors.black,
                                 ),
                                                                  onPressed: () async {
                                    try {
                                      final url = await ChatService.getOAuthLink('google', userId);
                                      // Close the modal first
                                      Navigator.of(context).pop();
                                      // Then open the OAuth link
                                      await launchUrl(
                                        Uri.parse(url),
                                        mode: LaunchMode.externalApplication,
                                      );
                                    } catch (e) {
                                      setModalState(() {
                                        error = e.toString();
                                      });
                                    }
                                  },
                                 child: const Text('Link'),
                               )
                             else
                               ElevatedButton(
                                 style: ElevatedButton.styleFrom(
                                   backgroundColor: Colors.red,
                                   foregroundColor: Colors.white,
                                 ),
                                                                   onPressed: () async {
                                    try {
                                      await AuthService.unlinkGoogle(userId);
                                      setModalState(() {
                                        googleStatus = 'Not linked';
                                        error = null;
                                      });
                                    } catch (e) {
                                      setModalState(() {
                                        error = e.toString();
                                      });
                                    }
                                  },
                                 child: const Text('Unlink'),
                               ),
                          ],
                        ),
                                                 if (error != null) ...[
                           const SizedBox(height: 12),
                           Text(error!, style: const TextStyle(color: Colors.red)),
                         ],
                         const SizedBox(height: 24),
                      ],
                    );
            },
          ),
        );
      },
    );
  }

  void _showDeleteConfirmation(BuildContext context, String chatCode) {
    showDialog(
      context: context,
      builder: (BuildContext context) {
        return AlertDialog(
          backgroundColor: Colors.grey[900],
          title: const Text(
            'Delete Chat',
            style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold),
          ),
          content: const Text(
            'Are you sure you want to delete this chat? This action cannot be undone.',
            style: TextStyle(color: Colors.white70),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: const Text(
                'Cancel',
                style: TextStyle(color: Colors.white70),
              ),
            ),
            TextButton(
              onPressed: () async {
                Navigator.of(context).pop();
                try {
                  await AuthService.deleteChat(widget.userId, chatCode);
                  
                  // Check if we're deleting the current chat
                  bool isCurrentChat = chatCode == widget.chatCode;
                  
                  // Refresh the chats list
                  setState(() {
                    _fetchChatsFuture = ChatService.fetchUserChats(widget.userId);
                  });
                  
                  // If we're deleting the current chat, navigate to the last available chat
                  if (isCurrentChat) {
                    final remainingChats = await ChatService.fetchUserChats(widget.userId);
                    if (remainingChats.isNotEmpty) {
                      // Navigate to the last chat
                      final lastChat = remainingChats.last;
                      if (context.mounted) {
                        Navigator.of(context).pushReplacement(
                          MaterialPageRoute(
                            builder: (_) => ChatScreen(
                              userId: widget.userId,
                              username: widget.username,
                              chatCode: lastChat['chatCode'],
                            ),
                          ),
                        );
                      }
                    } else {
                      // No chats left, create a new one
                      try {
                        final newChatCode = await ChatService.createNewChat(widget.userId);
                        if (context.mounted) {
                          Navigator.of(context).pushReplacement(
                            MaterialPageRoute(
                              builder: (_) => ChatScreen(
                                userId: widget.userId,
                                username: widget.username,
                                chatCode: newChatCode,
                              ),
                            ),
                          );
                        }
                      } catch (e) {
                        // If creating new chat fails, go back to auth screen
                        if (context.mounted) {
                          Navigator.of(context).pushAndRemoveUntil(
                            MaterialPageRoute(
                              builder: (_) => const AuthScreen(),
                            ),
                            (route) => false,
                          );
                        }
                      }
                    }
                  }
                } catch (e) {
                  // Show error message
                  if (context.mounted) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      SnackBar(
                        content: Text('Failed to delete chat: ${e.toString()}'),
                        backgroundColor: Colors.red,
                      ),
                    );
                  }
                }
              },
              child: const Text(
                'Delete',
                style: TextStyle(color: Colors.red),
              ),
            ),
          ],
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      key: _scaffoldKey,
      drawer: Drawer(
        backgroundColor: Colors.grey[900],
        child: SafeArea(
          child: FutureBuilder<List<Map<String, dynamic>>>(
            future: _fetchChatsFuture,
            builder: (context, snapshot) {
              final chats = snapshot.data ?? [];
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const SizedBox(height: 24),
                  Center(
                    child: CircleAvatar(
                      radius: 40,
                      backgroundColor: Colors.white,
                      child: Text(
                        widget.username.isNotEmpty ? widget.username[0].toUpperCase() : '?',
                        style: const TextStyle(fontSize: 36, color: Colors.black),
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Center(
                    child: Text(
                      widget.username,
                      style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18, color: Colors.white),
                    ),
                  ),
                  const SizedBox(height: 24),
                  if (chats.isNotEmpty) ...[
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 24),
                      child: Text('Your Chats', style: TextStyle(color: Colors.white70, fontWeight: FontWeight.bold)),
                    ),
                    const SizedBox(height: 8),
                    Expanded(
                      child: ListView.builder(
                        itemCount: chats.length,
                        itemBuilder: (context, index) {
                          final chat = chats[index];
                          final lastMsg = (chat['lastMessage'] as String?)?.trim() ?? '';
                          final title = lastMsg.isNotEmpty
                            ? lastMsg.split(' ').take(4).join(' ') + (lastMsg.split(' ').length > 4 ? '...' : '')
                            : 'New Chat';
                          bool isHovered = false;
                          return StatefulBuilder(
                            builder: (context, setState) {
                              return MouseRegion(
                                onEnter: (_) => setState(() => isHovered = true),
                                onExit: (_) => setState(() => isHovered = false),
                                child: Material(
                                  color: Colors.transparent,
                                  child: InkWell(
                                    onTap: () {
                                      Navigator.of(context).pushReplacement(
                                        MaterialPageRoute(
                                          builder: (_) => ChatScreen(
                                            userId: widget.userId,
                                            username: widget.username,
                                            chatCode: chat['chatCode'],
                                          ),
                                        ),
                                      );
                                    },
                                    borderRadius: BorderRadius.circular(8),
                                    highlightColor: Colors.white10,
                                    splashColor: Colors.white24,
                                                                         child: Container(
                                       margin: const EdgeInsets.symmetric(vertical: 2, horizontal: 8),
                                       decoration: BoxDecoration(
                                         color: isHovered ? Colors.white12 : Colors.white10,
                                         borderRadius: BorderRadius.circular(6),
                                       ),
                                       padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                                      child: Row(
                                        children: [
                                          Expanded(
                                            child: Text(title, style: const TextStyle(color: Colors.white)),
                                          ),
                                          IconButton(
                                            icon: const Icon(Icons.close, color: Colors.white70, size: 20),
                                            onPressed: () => _showDeleteConfirmation(context, chat['chatCode']),
                                            padding: EdgeInsets.zero,
                                            constraints: const BoxConstraints(minWidth: 24, minHeight: 24),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ),
                              );
                            },
                          );
                        },
                      ),
                    ),
                  ],
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 24),
                    child: ElevatedButton.icon(
                      icon: const Icon(Icons.person, color: Colors.black),
                      label: const Text('Manage Account', style: TextStyle(color: Colors.black)),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.white,
                        foregroundColor: Colors.black,
                        minimumSize: const Size.fromHeight(48),
                      ),
                      onPressed: _showManageAccountModal,
                    ),
                  ),
                  const SizedBox(height: 12),
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 24),
                    child: ElevatedButton.icon(
                      icon: const Icon(Icons.add, color: Colors.black),
                      label: const Text('Create New Chat', style: TextStyle(color: Colors.black)),
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.white,
                        foregroundColor: Colors.black,
                        minimumSize: const Size.fromHeight(48),
                      ),
                      onPressed: _createNewChat,
                    ),
                  ),
                                     const Spacer(),
                   Padding(
                     padding: const EdgeInsets.symmetric(horizontal: 24),
                     child: ElevatedButton.icon(
                       icon: const Icon(Icons.logout, color: Colors.white),
                       label: const Text('Log Out', style: TextStyle(color: Colors.white)),
                       style: ElevatedButton.styleFrom(
                         backgroundColor: Colors.red,
                         foregroundColor: Colors.white,
                         minimumSize: const Size.fromHeight(48),
                       ),
                       onPressed: () {
                         Navigator.of(context).pushAndRemoveUntil(
                           MaterialPageRoute(
                             builder: (_) => const AuthScreen(),
                           ),
                           (route) => false,
                         );
                       },
                     ),
                   ),
                   const SizedBox(height: 24),
                 ],
               );
             },
           ),
         ),
       ),
      appBar: PreferredSize(
        preferredSize: const Size.fromHeight(110),
        child: Container(
          color: const Color(0xFF181A20),
          padding: const EdgeInsets.only(top: 48, left: 8, right: 8),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              IconButton(
                icon: const Icon(Icons.menu, color: Colors.white, size: 28),
                onPressed: () {
                  _scaffoldKey.currentState?.openDrawer();
                },
              ),
              Row(
                children: [
                  const Text(
                    'GagaAgent',
                    style: TextStyle(
                      color: Colors.white,
                      fontWeight: FontWeight.bold,
                      fontSize: 20,
                    ),
                  ),
                  const SizedBox(width: 4),
                  const Icon(Icons.chevron_right, color: Colors.white70, size: 22),
                ],
              ),
              IconButton(
                icon: const Icon(Icons.edit, color: Colors.white, size: 24),
                onPressed: () {},
              ),
            ],
          ),
        ),
      ),
      body: Container(
        color: const Color(0xFF181A20), // Soft black background
        child: Column(
          children: [
            Expanded(
              child: Stack(
                children: [
                  ListView.builder(
                    reverse: true,
                    padding: const EdgeInsets.all(16),
                    itemCount: _messages.length,
                    itemBuilder: (context, index) {
                      final msg = _messages[_messages.length - 1 - index];
                      return _ChatBubble(message: msg);
                    },
                  ),
                  if (_showHint && _messages.isEmpty)
                    Center(
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
                        decoration: BoxDecoration(
                          color: Colors.white10,
                          borderRadius: BorderRadius.circular(16),
                        ),
                        child: const Text(
                          'Say hello to GagaAgent!',
                          style: TextStyle(color: Colors.white70, fontSize: 16),
                        ),
                      ),
                    ),
                ],
              ),
            ),
            if (_isSending)
              const Padding(
                padding: EdgeInsets.only(bottom: 8.0),
                child: _ThreeDotsLoading(),
              ),
            _ChatInputBox(
              controller: _controller,
              onSend: _isSending ? null : _sendMessage,
              isSending: _isSending,
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }
}

class _ChatMessage {
  final String text;
  final bool isUser;
  final DateTime time;
  _ChatMessage({required this.text, required this.isUser, required this.time});
}

class _ChatBubble extends StatelessWidget {
  final _ChatMessage message;
  const _ChatBubble({required this.message});

  @override
  Widget build(BuildContext context) {
    final align = message.isUser ? Alignment.centerRight : Alignment.centerLeft;
    final color = message.isUser ? const Color(0xFF262A34) : const Color(0xFF353941); // Soft greys
    final textColor = Colors.white;
    final radius = message.isUser
        ? const BorderRadius.only(
            topLeft: Radius.circular(18),
            topRight: Radius.circular(18),
            bottomLeft: Radius.circular(18),
          )
        : const BorderRadius.only(
            topLeft: Radius.circular(18),
            topRight: Radius.circular(18),
            bottomRight: Radius.circular(18),
          );
    return Container(
      margin: EdgeInsets.only(
        top: 8,
        bottom: 8,
        left: message.isUser ? 60 : 0,
        right: message.isUser ? 0 : 60,
      ),
      alignment: align,
      child: Column(
        crossAxisAlignment:
            message.isUser ? CrossAxisAlignment.end : CrossAxisAlignment.start,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            decoration: BoxDecoration(
              color: color,
              borderRadius: radius,
              boxShadow: [
                BoxShadow(
                  color: Colors.black.withOpacity(0.08),
                  blurRadius: 8,
                  offset: const Offset(2, 4),
                ),
              ],
            ),
                         child: message.isUser
                 ? Text(
                     message.text,
                     style: TextStyle(color: textColor, fontSize: 16),
                   )
                 : MarkdownBody(
                     data: message.text,
                     styleSheet: MarkdownStyleSheet(
                       p: TextStyle(color: textColor, fontSize: 16),
                     ),
                     onTapLink: (text, href, title) {
                       if (href != null) {
                         launchUrl(Uri.parse(href));
                       }
                     },
                   ),
          ),
          const SizedBox(height: 4),
          Text(
            _formatTime(message.time),
            style: TextStyle(color: Colors.grey[400], fontSize: 12),
          ),
        ],
      ),
    );
  }

  static String _formatTime(DateTime time) {
    final hour = time.hour.toString().padLeft(2, '0');
    final min = time.minute.toString().padLeft(2, '0');
    return '$hour:$min';
  }
}

class _ChatInputBox extends StatelessWidget {
  final TextEditingController controller;
  final VoidCallback? onSend;
  final bool isSending;
  const _ChatInputBox({required this.controller, required this.onSend, required this.isSending});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      child: Row(
        children: [
          Expanded(
            child: Container(
              decoration: BoxDecoration(
                color: Colors.white,
                borderRadius: BorderRadius.circular(24),
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withOpacity(0.05),
                    blurRadius: 4,
                    offset: const Offset(0, 2),
                  ),
                ],
              ),
              child: TextField(
                controller: controller,
                enabled: !isSending,
                decoration: const InputDecoration(
                  hintText: 'Type your message...',
                  border: InputBorder.none,
                  contentPadding: EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                ),
                onSubmitted: (_) => onSend?.call(),
              ),
            ),
          ),
          const SizedBox(width: 8),
          GestureDetector(
            onTap: isSending || onSend == null ? null : onSend,
            child: Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: Colors.white,
                shape: BoxShape.circle,
                boxShadow: [
                  BoxShadow(
                    color: Colors.white.withOpacity(0.15),
                    blurRadius: 8,
                    offset: const Offset(0, 2),
                  ),
                ],
              ),
              child: const Icon(Icons.send, color: Colors.black),
            ),
          ),
        ],
      ),
    );
  }
}

class _ThreeDotsLoading extends StatefulWidget {
  const _ThreeDotsLoading();
  @override
  State<_ThreeDotsLoading> createState() => _ThreeDotsLoadingState();
}

class _ThreeDotsLoadingState extends State<_ThreeDotsLoading> with SingleTickerProviderStateMixin {
  late AnimationController _controller;
  late Animation<int> _dotCount;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      duration: const Duration(milliseconds: 900),
      vsync: this,
    )..repeat();
    _dotCount = StepTween(begin: 1, end: 3).animate(
      CurvedAnimation(parent: _controller, curve: Curves.linear),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, child) {
        final dots = '.' * _dotCount.value;
        return Text(
          dots,
          style: const TextStyle(fontSize: 32, color: Colors.deepPurple),
        );
      },
    );
  }
}
