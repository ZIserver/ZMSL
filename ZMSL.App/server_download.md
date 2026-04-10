# 查询服务端分类

## OpenAPI Specification

```yaml
openapi: 3.0.1
info:
  title: ''
  description: ''
  version: 1.0.0
paths:
  /query/server_classify:
    get:
      summary: 查询服务端分类
      deprecated: false
      description: 查询支持的服务端的分类
      tags:
        - 服务端下载源
      parameters:
        - name: User-Agent
          in: header
          description: ''
          example: MSL API Test
          schema:
            type: string
            default: MSL API Test
      responses:
        '200':
          description: ''
          content:
            application/json:
              schema:
                type: object
                properties:
                  code:
                    type: integer
                  message:
                    type: string
                  data:
                    type: object
                    properties:
                      pluginsCore:
                        type: array
                        items:
                          type: string
                      pluginsAndModsCore:
                        type: array
                        items:
                          type: string
                      modsCore_Forge:
                        type: array
                        items:
                          type: string
                      modsCore_Fabric:
                        type: array
                        items:
                          type: string
                      vanillaCore:
                        type: array
                        items:
                          type: string
                      bedrockCore:
                        type: array
                        items:
                          type: string
                      proxyCore:
                        type: array
                        items:
                          type: string
                    required:
                      - pluginsCore
                      - pluginsAndModsCore
                      - modsCore_Forge
                      - modsCore_Fabric
                      - vanillaCore
                      - bedrockCore
                      - proxyCore
                required:
                  - code
                  - message
                  - data
              example:
                code: 200
                message: ''
                data:
                  pluginsCore:
                    - paper
                    - purpur
                    - spigot
                    - bukkit
                    - folia
                    - leaves
                    - pufferfish
                    - pufferfish_purpur
                    - pufferfishplus
                    - pufferfishplus_purpur
                    - spongevanilla
                  pluginsAndModsCore:
                    - arclight
                    - mohist
                    - catserver
                    - banner
                    - spongeforge
                  modsCore_Forge:
                    - forge
                    - neoforge
                  modsCore_Fabric:
                    - fabric
                    - quilt
                  vanillaCore:
                    - vanilla
                  bedrockCore:
                    - nukkitx
                  proxyCore:
                    - velocity
                    - bungeecord
                    - lightfall
                    - travertine
          headers: {}
          x-apifox-name: 成功
      security: []
      x-apifox-folder: 服务端下载源
      x-apifox-status: released
      x-run-in-apifox: https://app.apifox.com/web/project/4783705/apis/api-191469896-run
components:
  schemas: {}
  securitySchemes: {}
servers:
  - url: https://api.mslmc.cn/v3
    description: 正式环境
security: []

```


# 查询特定服务端支持的MC版本

## OpenAPI Specification

```yaml
openapi: 3.0.1
info:
  title: ''
  description: ''
  version: 1.0.0
paths:
  /query/available_versions/{server}:
    get:
      summary: 查询特定服务端支持的MC版本
      deprecated: false
      description: 查询特定服务端支持的MC版本
      tags:
        - 服务端下载源
      parameters:
        - name: server
          in: path
          description: 服务端
          required: true
          example: paper
          schema:
            type: string
        - name: User-Agent
          in: header
          description: ''
          example: MSL API Test
          schema:
            type: string
            default: MSL API Test
      responses:
        '200':
          description: ''
          content:
            application/json:
              schema:
                type: object
                properties:
                  code:
                    type: integer
                  message:
                    type: string
                  data:
                    type: object
                    properties:
                      versionList:
                        type: array
                        items:
                          type: string
                    required:
                      - versionList
                required:
                  - code
                  - message
                  - data
              example:
                code: 200
                message: ''
                data:
                  versionList:
                    - 1.20.6
                    - 1.20.5
                    - 1.20.4
                    - 1.20.2
                    - 1.20.1
                    - '1.20'
                    - 1.19.4
                    - 1.19.3
                    - 1.19.2
                    - 1.19.1
                    - '1.19'
                    - 1.18.2
                    - 1.18.1
                    - '1.18'
                    - 1.17.1
                    - '1.17'
                    - 1.16.5
                    - 1.16.4
                    - 1.16.3
                    - 1.16.2
                    - 1.16.1
                    - 1.15.2
                    - 1.15.1
                    - '1.15'
                    - 1.14.4
                    - 1.14.3
                    - 1.14.2
                    - 1.14.1
                    - '1.14'
                    - 1.13.2
                    - 1.13.1
                    - '1.13'
                    - 1.12.2
                    - 1.12.1
                    - '1.12'
                    - 1.11.2
                    - 1.10.2
                    - 1.9.4
                    - 1.8.8
          headers: {}
          x-apifox-name: 成功
      security: []
      x-apifox-folder: 服务端下载源
      x-apifox-status: released
      x-run-in-apifox: https://app.apifox.com/web/project/4783705/apis/api-191469918-run
components:
  schemas: {}
  securitySchemes: {}
servers:
  - url: https://api.mslmc.cn/v3
    description: 正式环境
security: []

```

# 获取服务端下载地址

## OpenAPI Specification

```yaml
openapi: 3.0.1
info:
  title: ''
  description: ''
  version: 1.0.0
paths:
  /download/server/{server}/{version}:
    get:
      summary: 获取服务端下载地址
      deprecated: false
      description: 获取特定类型和版本的服务端下载地址
      tags:
        - 服务端下载源
      parameters:
        - name: server
          in: path
          description: 服务端类型
          required: true
          example: pufferfishplus
          schema:
            type: string
        - name: version
          in: path
          description: MC版本
          required: true
          example: '1.20'
          schema:
            type: string
        - name: build
          in: query
          description: ''
          required: false
          example: latest
          schema:
            type: string
        - name: User-Agent
          in: header
          description: ''
          example: MSL API Test
          schema:
            type: string
            default: MSL API Test
      responses:
        '200':
          description: ''
          content:
            application/json:
              schema:
                type: object
                properties:
                  code:
                    type: integer
                  message:
                    type: string
                  data:
                    type: object
                    properties:
                      url:
                        type: string
                      sha256:
                        type: string
                        description: 仅当服务端存在MSL镜像源时才会返回
                    required:
                      - url
                    x-apifox-orders:
                      - url
                      - sha256
                required:
                  - code
                  - message
                  - data
                x-apifox-orders:
                  - code
                  - message
                  - data
              example:
                code: 200
                message: ''
                data:
                  url: >-
                    https://api.mslmc.cn/v2/files/pufferfishplus/pufferfishplus-1.20-40.jar
                  sha256: >-
                    a73684f3d4ce29c2714921fa339b130b977ad8dedefdf33f46544fa514a6ea87
          headers: {}
          x-apifox-name: 成功
      security: []
      x-apifox-folder: 服务端下载源
      x-apifox-status: released
      x-run-in-apifox: https://app.apifox.com/web/project/4783705/apis/api-191469985-run
components:
  schemas: {}
  securitySchemes: {}
servers:
  - url: https://api.mslmc.cn/v3
    description: 正式环境
security: []

```