# README media

- `banner.png` (2560×1280) and `social-preview.png` (1280×640, for the repository's social preview):
  rendered from `banner.html` with headless Chrome, in the website's design (`web/DESIGN.md`):
  ```sh
  chrome --headless=new --hide-scrollbars --force-device-scale-factor=2 --window-size=1280,640 --screenshot=banner.png banner.html
  ```
- `demo.gif` (480×600, 15 s, silent): the first scenes of the LinkedIn cut of the launch film,
  converted with ffmpeg (12 fps, 96-colour palette). It's an illustration; the people and
  workspaces in it are made up, and it says so in the frame.

App icons in the illustrations are letter tiles, never the apps' own logos.
