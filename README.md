game framework thing that was born because XNA/FNA kind of sucked, currently in early active development and not very usable yet, built on top of SDL3 and WebGPU

TODO delete all of this and make a proper readme

```sh
git submodule update --init --recursive # if you haven't already cloned them yet
make -C Injure.Native/Native RID=linux-x64 # replace linux-arm64 with one of: osx-x64, osx-arm64, linux-x64, linux-arm64
```
you only need to do this once, unless you wanna update/rebuild them. yes, only macos and linux are supported right now, sorry
