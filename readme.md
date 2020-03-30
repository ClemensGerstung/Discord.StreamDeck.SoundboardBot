# Stream-Deck Soundboard for Discord

### Command to generate the gRPC files

#### For NodeJS

```cmd
grpc_tools_node_protoc --js_out=import_style=commonjs,binary:.\discord --grpc_out=.\discord --plugin=protoc-gen-grpc=%appdata%\npm\grpc_tools_node_protoc_plugin.cmd soundboard.proto
```

#### For C++

```cmd

```