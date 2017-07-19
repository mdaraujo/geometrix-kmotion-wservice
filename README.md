# Geometrix KMOTION WService

This project provides an input controller for "Kinect for Windows v2".

## HandController (Debug)

This application provides an UI for testing the hand controller server.
The green ball is the engaged hand.

## HandControllerServer

This is the main application used to communicate with clients through websockets.
The messages contains the position and the closed state of the engaged hand, in JSON.