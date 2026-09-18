# Unity FYP VR Room Project
A VR haircutting simulation developed in Unity and C# for my Final Year Project.

# ✂️ Tress Tamer – VR Haircutting Training Game

**Tress Tamer** is a Virtual Reality (VR) haircutting training game developed as my Final Year Project for the **Bachelor of Computer Science (Honours) in Interactive Software Technology** at Tunku Abdul Rahman University of Management and Technology (TAR UMT).

The project provides an interactive VR environment where players can practise haircutting techniques using virtual hairstyling tools. It combines segmented hair-card cutting, hairstyle-specific training guidance, hair sectioning, length measurement, and real-time haircut evaluation.

## 🎮 Key Features

- **VR Haircutting Interaction**
  - Grab and interact with hairstyling tools using VR controllers.
  - Designed using Unity XR Interaction Toolkit and OpenXR.

- **Precision Scissor Cutting**
  - Hair cards are divided into multiple segments.
  - Players can progressively shorten individual hair cards using scissors.

- **Clipper & Guard System**
  - Continuous hair cutting using a virtual clipper.
  - Adjustable clipper guards control the resulting hair length.
  - Very short hair can transition into stubble states.

- **Comb Interaction**
  - Use the comb to interact with hair and assist with length measurement and cutting.

- **Hair Sectioning**
  - Hair clips can gather and protect selected hair zones while other sections are being cut.

- **20 Hair Zones**
  - Hair is organized into zones across the back, sides, crown, top, and fringe.
  - Head-zone visualization helps players identify the area they are currently working on.

- **Step-by-Step Training Guidance**
  - Hairstyle-specific instructions guide the player through each hair zone.
  - Displays the current zone, recommended tool, target length, and cutting instructions.

- **Real-Time Haircut Evaluation**
  - Compares the current hair length against hairstyle-specific target lengths.
  - Provides real-time haircut accuracy and performance grading.

- **Undo & Reset**
  - Undo previous haircut actions.
  - Reset the hairstyle and restore the hair state when required.

## 💇 Available Hairstyles

Tress Tamer currently includes six target hairstyles:

1. Induction Cut
2. Butch Cut
3. Crew Cut
4. Standard Bob
5. A-Line Bob
6. Disconnected Undercut

Each hairstyle uses different target hair lengths and cutting requirements across the hair zones.

## 🧠 Hair Cutting System

Instead of treating the hairstyle as a single static mesh, Tress Tamer uses a **segmented hair-card system**.

Individual hair cards are divided into multiple cuttable segments. When the player cuts a hair card, the system determines the nearest segment to the cutting position and removes the appropriate segments toward the tip.

This allows the game to represent progressive hair shortening while maintaining control over the resulting hair length.

The haircut system also supports:

- Per-zone target lengths
- Multi-level length configurations
- Individual hair-card length overrides
- Clipper guard lengths
- Stubble transitions
- Hairstyle-specific evaluation

## 🛠️ Technologies Used

| Technology | Purpose |
|---|---|
| Unity | Game development |
| C# | Gameplay and system programming |
| OpenXR | VR platform integration |
| XR Interaction Toolkit | VR grabbing and interaction |
| Blender | 3D modelling and hair-card preparation |
| Shader Graph | Hair rendering |

## 🥽 VR Hardware

The project was developed and tested as a VR application using a head-mounted VR system and motion controllers.

## 📁 Main Systems

Some of the major systems implemented in the project include:

- VR Interaction System
- Hair Card & Hair Section System
- Hairstyling Tool System
- Hairstyle Generation & Hair Length Control
- Undo & State Restoration
- Head Zone Visualization
- Hairstyle Accuracy Evaluation
- Haircutting Training Guidance System

## ▶️ Running the Project

### Requirements

- Unity 2022
- OpenXR-compatible VR setup
- Compatible VR headset and controllers

## Project Structure

The repository contains the Unity project source files required to open and develop Tress Tamer.

Important Unity directories include:

- `Assets/` – Game scripts, scenes, models, materials, audio and other assets
- `Packages/` – Unity package configuration
- `ProjectSettings/` – Unity project configuration

Large 3D models and the main Unity scene are managed using **Git LFS**.

Unity-generated folders such as `Library`, `Temp`, `Logs`, and build outputs are intentionally excluded from the repository.

## Running the Project

1. Clone the repository using Git with Git LFS installed.
2. Open the project using the appropriate Unity Editor version.
3. Allow Unity to restore the required packages and regenerate the `Library` folder.
4. Open the main Tress Tamer scene.
5. Connect a supported VR headset and run the project.
