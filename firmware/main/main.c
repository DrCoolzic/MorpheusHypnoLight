/*
 * Sequence playback demo for the Morpheus HypnoLight prototype.
 *
 * This program initializes the LED hardware, runs the legacy hardware tests
 * stored in test.c, loads a short sequence, starts playback, and then opens
 * an interactive console so start/stop/pause/seek commands can be sent from
 * the serial terminal.
 */

#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "esp_console.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "led_control.h"
#include "led_engine.h"
#include "oscillator.h"
#include "sequence.h"
#include "test.h"
#include "test_sequence.h"

static const char *TAG = "sequence_demo";

#define OSCILLATOR_TICK_PERIOD_US 1000

/**
 * @brief Generate waveform samples and apply them to the LED outputs.
 *
 * This 1 kHz timer callback feeds the led_engine; the sequence component uses
 * its own timer to advance steps.
 *
 * @param[in] arg Unused esp_timer callback argument.
 */
static void oscillator_timer_callback(void *arg) {
  (void)arg;

  ESP_ERROR_CHECK(led_engine_tick());
}

/* --- Console commands ---------------------------------------------------- */

static int cmd_sequence_play(int argc, char **argv) {
  (void)argc;
  (void)argv;

  const esp_err_t error = sequence_play();
  if (error != ESP_OK) {
    printf("sequence_play failed: %d\n", error);
    return 1;
  }
  printf("sequence playing\n");
  return 0;
}

static int cmd_sequence_pause(int argc, char **argv) {
  (void)argc;
  (void)argv;

  const esp_err_t error = sequence_pause();
  if (error != ESP_OK) {
    printf("sequence_pause failed: %d\n", error);
    return 1;
  }
  printf("sequence paused\n");
  return 0;
}

static int cmd_sequence_stop(int argc, char **argv) {
  (void)argc;
  (void)argv;

  /* Stop is equivalent to pause; use seek 0 to return to the beginning. */
  esp_err_t error = sequence_pause();
  if (error != ESP_OK) {
    printf("sequence_stop failed: %d\n", error);
    return 1;
  }
  error = sequence_seek(0U);
  if (error != ESP_OK) {
    printf("sequence_seek(0) failed: %d\n", error);
    return 1;
  }

  /* Force every oscillator to a static zero output so the LEDs stay off
   * even though the 1 kHz led_engine tick keeps running. */
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    ESP_ERROR_CHECK(led_engine_set_frequency(oscillator_id, 0.0f));
    ESP_ERROR_CHECK(led_engine_set_brightness(oscillator_id, 0.0f));
  }
  ESP_ERROR_CHECK(led_engine_all_off());

  printf("sequence stopped\n");
  return 0;
}

static int cmd_sequence_seek(int argc, char **argv) {
  if (argc < 2) {
    printf("Usage: seek <position_ms>\n");
    return 1;
  }

  const uint32_t position_ms = (uint32_t)strtoul(argv[1], NULL, 10);
  const esp_err_t error = sequence_seek(position_ms);
  if (error != ESP_OK) {
    printf("sequence_seek failed: %d\n", error);
    return 1;
  }
  printf("sequence seeked to %lu ms (step %lu)\n", (unsigned long)position_ms,
         (unsigned long)sequence_get_current_step());
  return 0;
}

static int cmd_sequence_status(int argc, char **argv) {
  (void)argc;
  (void)argv;

  printf("playing: %s, step: %lu, position: %lu ms\n",
         sequence_is_playing() ? "yes" : "no",
         (unsigned long)sequence_get_current_step(),
         (unsigned long)sequence_get_elapsed_ms());
  return 0;
}

static int cmd_brightness(int argc, char **argv) {
  if (argc < 2) {
    printf("Usage: bright <value>\n");
    printf("Current global brightness: %.2f\n",
           (double)led_control_get_global_brightness());
    return 1;
  }

  const float brightness = strtof(argv[1], NULL);
  if (!isfinite(brightness) || brightness < 0.0f || brightness > 1.0f) {
    printf("Brightness must be between 0.0 and 1.0\n");
    return 1;
  }

  const esp_err_t error = led_control_set_global_brightness(brightness);
  if (error != ESP_OK) {
    printf("led_control_set_global_brightness failed: %d\n", error);
    return 1;
  }

  printf("Global brightness set to %.2f\n", (double)brightness);
  return 0;
}

static int cmd_run_tests(int argc, char **argv) {
  (void)argc;
  (void)argv;

  /* test_run_all receives the oscillator timer so it can start/stop the
   * 1 kHz led_engine tick during the visual tests. */
  extern esp_timer_handle_t g_oscillator_timer;
  printf("Running hardware tests...\n");
  test_run_all(g_oscillator_timer);
  printf("Hardware tests complete\n");

  /* Restart the led_engine tick for sequence playback. */
  ESP_ERROR_CHECK(
      esp_timer_start_periodic(g_oscillator_timer, OSCILLATOR_TICK_PERIOD_US));
  return 0;
}

static void register_sequence_commands(void) {
  const esp_console_cmd_t commands[] = {
      {
          .command = "play",
          .help = "Start or resume sequence playback",
          .hint = NULL,
          .func = &cmd_sequence_play,
      },
      {
          .command = "pause",
          .help = "Pause sequence playback",
          .hint = NULL,
          .func = &cmd_sequence_pause,
      },
      {
          .command = "stop",
          .help = "Stop playback and return to the beginning",
          .hint = NULL,
          .func = &cmd_sequence_stop,
      },
      {
          .command = "seek",
          .help = "Seek to an absolute position in milliseconds",
          .hint = "<position_ms>",
          .func = &cmd_sequence_seek,
      },
      {
          .command = "status",
          .help = "Show playback status and current step",
          .hint = NULL,
          .func = &cmd_sequence_status,
      },
      {
          .command = "bright",
          .help = "Set global brightness multiplier (0.0 to 1.0)",
          .hint = "<brightness>",
          .func = &cmd_brightness,
      },
      {
          .command = "tests",
          .help = "Run the hardware tests stored in test.c",
          .hint = NULL,
          .func = &cmd_run_tests,
      },
  };

  for (size_t i = 0; i < sizeof(commands) / sizeof(commands[0]); i++) {
    ESP_ERROR_CHECK(esp_console_cmd_register(&commands[i]));
  }
}

/* Global handle used by console commands and the demo setup. */
esp_timer_handle_t g_oscillator_timer = NULL;

void app_main(void) {
  ESP_LOGI(TAG, "Initializing LED hardware");
  ESP_ERROR_CHECK(led_control_init());
  ESP_ERROR_CHECK(
      led_control_set_global_brightness(1.0f)); /* limit eye strain */
  ESP_ERROR_CHECK(led_engine_init());
  ESP_ERROR_CHECK(led_control_all_off());

  const esp_timer_create_args_t oscillator_timer_args = {
      .callback = oscillator_timer_callback,
      .arg = NULL,
      .name = "led_engine_tick",
  };
  ESP_ERROR_CHECK(
      esp_timer_create(&oscillator_timer_args, &g_oscillator_timer));

  ESP_LOGI(TAG, "Starting 1 kHz led_engine tick");
  ESP_ERROR_CHECK(
      esp_timer_start_periodic(g_oscillator_timer, OSCILLATOR_TICK_PERIOD_US));

  /* Optional: run the legacy hardware tests once before the demo starts.
   * If enabled, test_run_all() stops the oscillator timer, so restart it
   * afterwards with esp_timer_start_periodic(). */
  // test_run_all(g_oscillator_timer);
  // ESP_ERROR_CHECK(
  //     esp_timer_start_periodic(g_oscillator_timer,
  //     OSCILLATOR_TICK_PERIOD_US));

  /* Load and start the demo sequence. */
  sequence_step_t demo_sequence[SEQUENCE_DEMO_STEP_COUNT];
  build_demo_sequence(demo_sequence);

  ESP_LOGI(TAG, "Loading demo sequence");
  ESP_ERROR_CHECK(sequence_init());
  ESP_ERROR_CHECK(sequence_load(demo_sequence, SEQUENCE_DEMO_STEP_COUNT));
  ESP_ERROR_CHECK(sequence_play());
  ESP_LOGI(TAG, "Sequence started. Open the serial console and type help for "
                "commands (play, pause, stop, seek, status, tests).");

  /* Interactive console. */
  esp_console_repl_t *repl = NULL;
  esp_console_repl_config_t repl_config = ESP_CONSOLE_REPL_CONFIG_DEFAULT();
  esp_console_dev_uart_config_t uart_config =
      ESP_CONSOLE_DEV_UART_CONFIG_DEFAULT();
  ESP_ERROR_CHECK(esp_console_new_repl_uart(&uart_config, &repl_config, &repl));
  ESP_ERROR_CHECK(esp_console_register_help_command());
  register_sequence_commands();
  ESP_ERROR_CHECK(esp_console_start_repl(repl));
}