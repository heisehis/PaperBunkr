#!/usr/bin/env bash
# mux.sh <video_noaudio> <music.wav> <poster frame number> <output basename>
# Bakes the chosen frame in as frame 0 (the thumbnail), adds the music, writes <out>.mp4 and <out>.jpg one level up.
set -e
cd "$(dirname "$0")"
v=$1; a=$2; n=$3; out=../$4
ffmpeg -y -loglevel error -i "$v" -vf "select=eq(n\,$n)" -frames:v 1 "poster_$4.png"
ffmpeg -y -loglevel error -i "poster_$4.png" -q:v 2 "$out.jpg"
ffmpeg -y -loglevel error -i "$v" -loop 1 -i "poster_$4.png" -i "$a" \
  -filter_complex "[0:v][1:v]overlay=enable='eq(n,0)':shortest=1,format=yuv420p[v]" \
  -map "[v]" -map 2:a -c:v libx264 -preset slow -crf 18 -r 30 -c:a aac -b:a 256k -movflags +faststart "$out.mp4"
ffprobe -v error -show_entries stream=codec_type,width,height,nb_frames,duration -of csv=p=0 "$out.mp4"
