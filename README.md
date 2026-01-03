# GradientNav
GradientNav is an open-source project initially developed for the Bionic Vision Lab at the University of California, Santa Barbara, designed to facilitate research on sensory/gradient-based navigation.

The most up-to-date build of the study may be found under 'Build & Patch History/', which contains the setup for conducting the original study (refer to "Original Gamemodes & Objectives" for details). Download of the entire repository for the source code is only necessary for those interested in modifying or expanding the study (see "Source Code & Unity Asset Store Policy" for instructions on how to rebuild the project).

# Original Gamemodes & Objectives
The participant is tasked with a sensory substitution/augmentation task. Only able to see a single, gradient color, which changes depending on position, their goal is to find the brightest point in a map using only luminance cues. In other words, we wish to find how well a participant can navigate to the brightest point of a map given only a single degree of freedom for stimuli.

Gamemodes are currently under development. The current study supports a basic study with a few different luminance functions. The center is randomized, and the brightest point must be found under a time trial. 

# Source Code & Unity Asset Store Policy
In adherence with the Unity Asset Store Terms of Service, which prohibits the redistribution of Store assets, several Package Manager Assets and Asset Store Plugins have not been included in this open-source build. As such, the project does not compile immediately upon download. Perform the following steps if you wish to reconstruct the project:

1) Install the following assets:
    - From Window > Asset Store:
        - SteamVR Plugin

2) Configure the above assets and other dependencies:
	- Set XR to be initialized on startup via Edit > Project Settings > XR Plug-in Management > Initialize XR on Startup. Also ensure that "OpenVR Loader" and "OpenXR" are checkmarked.

If any errors with dependencies are spotted at this point, run Assets > Reimport All.

# Attribution
This project is licensed under the Apache License 2.0. 

You are free to use, modify, and distribute this software, but credit/attribution is required. 
