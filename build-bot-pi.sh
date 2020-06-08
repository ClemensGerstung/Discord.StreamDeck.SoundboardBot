#!/usr/bin/env bash
export PROTOBUF_PROTOC = $(which protoc)

if [[ -z "PROTOBUF_PROTOC" ]]; then
  echo "protoc not found on system"
  exit 1
fi

dotnet publish ./discord/Discord.csproj -r linux-arm -p:PublishReadyToRun=true -o ../output

cp /usr/local/lib/libsodium.so ../output
cp /usr/local/lib/libopus.so ../output

# check when building on your own
cp /libgrpc_csharp_ext/grpc/cmake/build/libgrpc_csharp_ext.so ../output/libgrpc_csharp_ext.x86.so
