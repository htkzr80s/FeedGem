FeedGem is a simple 3-pane RSS reader developed by an individual with no prior programming experience,
built entirely through AI-assisted coding. It is a lightweight, portable application featuring multi-language support and a dark theme.

Disclaimer

Please read the following carefully before using this software:

    Experimental Project: This application is intended for learning and experimental purposes.
    There is no guarantee of stability or completeness as a production-grade software.
    No Warranty: The author assumes no responsibility for any damages (data loss, PC malfunctions, etc.) incurred through the use of this software.
    No Support: As the developer is a non-programmer, individual bug fixes, feature requests, or technical support cannot be provided.
    Open for Modification: Feel free to modify, redistribute, or fork this project as you wish.

Environment

    OS: Windows (x64 only)
    Framework: WPF
    Prerequisites: .NET 10.0 Runtime

Key Features

    Portable: No installation required.
    Multi-language: English and Japanese built-in. Easily extendable to other languages via JSON files.
    Folder Management: Organize feeds using single-level folders.
    Feed Discovery: Automatically find RSS feeds from websites.
    Dark Theme: Support for dark mode UI.
    OPML Support: Import and export your feed lists.

Current Limitations

    Keyboard shortcuts are not supported.
    Fine-grained customization and advanced settings are unavailable.

How to Use

Register Feeds: Enter a URL into the text box at the bottom to register an RSS feed.  
Discover Feeds: The app can also search for and discover multiple feeds from a single URL.  
Automatic Updates: Feeds are updated automatically at startup, when waking from sleep, and every hour.  
Settings: Click the gear icon in the bottom-right corner to open the settings menu.  
Adding Languages: You can add new languages to the dropdown menu by editing sample.en.json and placing it in the Language folder.  
Note: Ensure that the "language" and "locale" fields within the _metadata section of the JSON file are filled in.  
Pro-tip: Missing translations will be automatically filled with English.
	It may be easiest to provide the sample.en.json file to an AI and ask it to translate the content for you.  


Advanced Features

Manual Configuration: Certain settings can be modified by directly editing the .ini file.  
Troubleshooting: If the application behaves unexpectedly after making changes,
	please close the app completely and delete the .ini file to reset your settings.  

Advanced Settings Parameters

MaxArticleCount=30: The maximum number of articles to store in the database for each feed.  

AllowInsecureHttp=False: Set whether to allow unencrypted communication (HTTP).


Libraries (NuGet)

This project utilizes the following libraries:

System.ServiceModel.Syndication
Microsoft.Data.Sqlite
HtmlAgilityPack
Microsoft.Web.WebView2
H.NotifyIcon.Wpf


License

This project is licensed under the MIT License. See the LICENSE file for more details.

© 2026 htkzr80s