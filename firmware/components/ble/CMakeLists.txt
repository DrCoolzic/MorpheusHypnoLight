idf_component_register(SRCS "ble.c"
                       INCLUDE_DIRS "include"
                       REQUIRES bt nvs_flash sequence led_control led_engine)
