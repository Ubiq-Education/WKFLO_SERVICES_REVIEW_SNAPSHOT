# Chat Widget Configuration

This document explains how to configure the Chat Widget in the AMAIS WKFLO MD WebAPI project.

## Environment Variables

The Chat Widget requires the following environment variable to be set:

### ChatWidgetSettings__WidgetScriptUrl

This environment variable specifies the URL for the chat widget script that will be loaded on the chat page.

**Default Value:** `http://localhost/assets/wkflo-widget.umd.cjs`

### Setting Environment Variables

#### Option 1: Using appsettings.json (Development)
```json
{
  "ChatWidgetSettings": {
    "WidgetScriptUrl": "https://your-cdn.com/assets/wkflo-widget.umd.cjs"
  }
}
```

#### Option 2: Environment Variables (Production)
Set the environment variable:
- **Windows:** `set ChatWidgetSettings__WidgetScriptUrl=https://your-cdn.com/assets/wkflo-widget.umd.cjs`
- **Linux/Mac:** `export ChatWidgetSettings__WidgetScriptUrl=https://your-cdn.com/assets/wkflo-widget.umd.cjs`
- **Azure App Service:** Set in Application Settings as `ChatWidgetSettings__WidgetScriptUrl`

#### Option 3: Docker
```dockerfile
ENV ChatWidgetSettings__WidgetScriptUrl=https://your-cdn.com/assets/wkflo-widget.umd.cjs
```

## Database Configuration

All other chat widget settings come from the `AgentChatSetting` database table based on the `ShareId` parameter:

- **HeaderTitle** - Title text shown in the widget header
- **ChatWidth** - Widget width setting (e.g., "standard"); optional, falls back to a default if not set
- **PrimaryColor** - The primary color for the chat widget
- **ForegroundColor** - Foreground/text color for the chat widget
- **BubbleStyle** - The bubble style (e.g., "rounded", "square"); optional, falls back to a default if not set
- **FloatingPosition** - Where the launcher bubble floats (e.g., "right"); optional, falls back to a default if not set
- **LauncherStyle** - The launcher button style (e.g., "circle"); optional, falls back to a default if not set
- **LauncherText** - Text label on the launcher button
- **BubbleIconPath** - Path/URL to the launcher bubble icon
- **ChatbotIconPath** - Path/URL to the chatbot's avatar icon
- **CssUrl** - URL to a custom CSS override stylesheet
- **WelcomeMessage** - Message shown when the widget is first opened
- **FooterMessage** - Message shown in the widget footer
- **PredefinedPrompts** - Collection of suggested prompts shown to the user
- **ShowDescription** - Whether to show the agent description
- **ChainOfThought** - Gets or sets a value indicating whether the chain of thought process is enabled.
- **IsWidget** - Whether this configuration is exposed as an embeddable widget
- **ApiEndpoint** - The API endpoint the widget should call
- **SecurityConfiguration** - Security configuration (public, Azure AD, API Key)
- **IsShareable** - Whether this configuration is publicly reachable via `ShareId`
- **ShareId** - The public identifier used in the `/chat/{shareId}` route
- **AgentName** - From the associated Agent record
- **AgentDescription** - From the associated Agent record

## Usage

The chat widget can be accessed via: `/chat/{shareId}`

Where `{shareId}` is the ShareId from the AgentChatSetting table that has `IsShareable = true`.

## Implementation Details

The ChatController:
1. Uses `IMediator` to query the database for AgentChatSetting by ShareId
2. Combines database settings with environment configuration
3. Returns a ChatViewModel to the view with all necessary configuration

The view model includes both database-driven properties and environment-driven properties, providing a flexible configuration system.