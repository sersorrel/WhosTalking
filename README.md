# Who's Talking

[![Download count](https://img.shields.io/endpoint?url=https://qzysathwfhebdai6xgauhz4q7m0mzmrf.lambda-url.us-east-1.on.aws/WhosTalking)](https://github.com/sersorrel/WhosTalking)

_an ffxiv plogon_

See Discord voice activity indicators directly on your party list.

## Usage

Install from the in-game plugin installer.

## Development

### Discord RPC auth flow

See https://discord.com/developers/docs/topics/rpc for details.

- We make a WebSocket connection to the Discord client
- Discord sends us a `READY` event
- We send an `AUTHENTICATE` command with our access token
- Discord echoes back our `AUTHENTICATE`, with details of the logged-in user
  - there's more complexity here if we aren't yet authorised to use RPC
- We subscribe to `VOICE_CHANNEL_SELECT` events
- We send a `GET_SELECTED_VOICE_CHANNEL` command, to find out if the user is in voice
- Discord echoes back our `GET_SELECTED_VOICE_CHANNEL`, with details of the user's current voice channel
- *If the user is not in a voice channel:*
  - We clear `Channel`
  - We clear `AllUsers`
    - NB: we also do this if a `VOICE_CHANNEL_SELECT` event indicates the user is no longer in voice
- *If the user is in a voice channel:*
  - We set `Channel`
  - We recreate `AllUsers` based on the data from Discord

## Licensing and Attribution

This plugin contains some icons from [Google Fonts' Material Design icon library](https://fonts.google.com/icons), which are utilized in this project under the Apache 2.0 license.
